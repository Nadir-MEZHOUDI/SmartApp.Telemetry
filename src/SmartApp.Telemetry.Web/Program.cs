using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SmartApp.Telemetry.Infrastructure;
using SmartApp.Telemetry.Web;
using SmartApp.Telemetry.Web.Endpoints;
using SmartApp.Telemetry.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = false;
});

var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");
var connectionString = builder.Configuration.GetConnectionString("Telemetry")
    ?? "Host=localhost;Database=Telemetry;Username=postgres;Password=postgres";
var adminKey = builder.Configuration["Dashboard:AdminKey"] ?? builder.Configuration["TelemetryApi:AdminKey"] ?? string.Empty;
var dashboardPassword = builder.Configuration["Dashboard:Password"] ?? string.Empty;

if (useInMemory)
    builder.Services.AddDbContext<TelemetryDbContext>(options => options.UseInMemoryDatabase("telemetry-tests"));
else
    builder.Services.AddDbContext<TelemetryDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddScoped<TelemetryIngestionService>();
builder.Services.AddScoped<TelemetryDashboardService>();
builder.Services.AddScoped<TelemetryAggregationService>();
builder.Services.AddHostedService<TelemetryMaintenanceService>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "SmartAppTelemetry.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<TelemetryApiClient>();
builder.Services.AddScoped<DashboardState>();
builder.Services.AddHealthChecks().AddDbContextCheck<TelemetryDbContext>();
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(options =>
{
    var ingestionLimit = Math.Max(1, builder.Configuration.GetValue("Telemetry:IngestionRateLimitPerMinute", 120));
    var loginLimit = Math.Max(1, builder.Configuration.GetValue("Telemetry:LoginRateLimitPerMinute", 10));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiting");
        RateLimitLog.Rejected(logger, context.HttpContext.Request.Path.ToString(), ClientKey(context.HttpContext));
        if (!context.HttpContext.Response.HasStarted)
            await context.HttpContext.Response.WriteAsJsonAsync(
                new { error = "Too many requests. Please retry shortly." },
                cancellationToken);
    };
    options.AddPolicy("ingestion", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ingestionLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = loginLimit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
    if (useInMemory) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseRateLimiter();
app.UseAuthentication();

app.Use(async (context, next) =>
{
    if (!IsPublicRequest(context.Request.Path))
    {
        if (string.IsNullOrWhiteSpace(dashboardPassword))
        {
            await RedirectToLoginAsync(context, "not-configured");
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            if (AcceptsHtml(context.Request))
                await RedirectToLoginAsync(context);
            else
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }

    await next();
});
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1/dashboard", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(adminKey) &&
        !FixedTimeEquals(adminKey, context.Request.Headers["X-Admin-Key"].ToString()))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Dashboard authentication required." });
        return;
    }
    await next();
});

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

app.MapApiEndpoints(dashboardPassword);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string ClientKey(HttpContext context) =>
    context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
    ?? context.Connection.RemoteIpAddress?.ToString()
    ?? "unknown";

static bool IsPublicRequest(PathString path) =>
    path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWithSegments("/login", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWithSegments("/logout", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase);

static bool AcceptsHtml(HttpRequest request) =>
    request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

static async Task RedirectToLoginAsync(HttpContext context, string? error = null)
{
    if (!AcceptsHtml(context.Request))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = "Dashboard password is not configured." });
        return;
    }

    var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
    context.Response.Redirect(DashboardAuthentication.LoginUrl(returnUrl, error));
}

static bool FixedTimeEquals(string configured, string supplied)
{
    var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
    return CryptographicOperations.FixedTimeEquals(configuredHash, suppliedHash);
}

namespace SmartApp.Telemetry.Web
{
    public sealed record ResolveErrorRequest(string? Version);

    public partial class Program;

    internal static class RateLimitLog
    {
        private static readonly Action<Microsoft.Extensions.Logging.ILogger, string, string, Exception?> RejectedMessage =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(1, "RateLimitRejected"),
                "Rate limit rejected {Path} for client {Client}.");

        public static void Rejected(Microsoft.Extensions.Logging.ILogger logger, string path, string client) =>
            RejectedMessage(logger, path, client, null);
    }
}
