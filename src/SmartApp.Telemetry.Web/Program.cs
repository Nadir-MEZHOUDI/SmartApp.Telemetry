using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SmartApp.Telemetry.Core;
using SmartApp.Telemetry.Infrastructure;
using SmartApp.Telemetry.Web;
using SmartApp.Telemetry.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = false;
});

var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");
var connectionString = builder.Configuration.GetConnectionString("Telemetry")
    ?? "Host=localhost;Database=Telemetry;Username=postgres;Password=postgres";
var telemetryApiBaseUrl = builder.Configuration["TelemetryApi:BaseUrl"] ?? "http://localhost:5000";
var adminKey = builder.Configuration["Dashboard:AdminKey"] ?? builder.Configuration["TelemetryApi:AdminKey"] ?? string.Empty;
var dashboardPassword = builder.Configuration["Dashboard:Password"] ?? string.Empty;

if (useInMemory)
    builder.Services.AddDbContext<TelemetryDbContext>(options => options.UseInMemoryDatabase("telemetry-tests"));
else
    builder.Services.AddDbContext<TelemetryDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddScoped<TelemetryIngestionService>();
builder.Services.AddScoped<TelemetryDashboardService>();
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
builder.Services.AddHttpClient<TelemetryApiClient>(client =>
{
    client.BaseAddress = new Uri(telemetryApiBaseUrl.TrimEnd('/') + "/");
    if (!string.IsNullOrWhiteSpace(adminKey))
        client.DefaultRequestHeaders.Add("X-Admin-Key", adminKey);
});
builder.Services.AddScoped<DashboardState>();
builder.Services.AddHealthChecks().AddDbContextCheck<TelemetryDbContext>();
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("ingestion", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 10;
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
    if (context.Request.Path.StartsWithSegments("/api/v1/dashboard") &&
        !string.IsNullOrWhiteSpace(adminKey) &&
        !string.Equals(context.Request.Headers["X-Admin-Key"].ToString(), adminKey, StringComparison.Ordinal))
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

app.MapGet("/api", () => Results.Ok(new
{
    service = "SmartApp Telemetry API",
    status = "ok",
    docs = "/openapi/v1.json"
}));

app.MapGet("/api/v1/applications", async (TelemetryDbContext db, CancellationToken cancellationToken) =>
{
    var applications = await db.Applications.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    var result = applications.Select(x => new { x.Id, x.Name, x.Slug, x.Description, x.IsEnabled, x.CreatedAt });
    return Results.Ok(result);
});

app.MapPost("/api/v1/applications", async (
    CreateApplicationRequest request,
    TelemetryDbContext db,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
        return Results.BadRequest(new { error = "Name and Slug are required." });

    var slug = request.Slug.Trim().ToLowerInvariant();
    if (slug.Length > 100 || slug.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
        return Results.BadRequest(new { error = "Slug may contain only letters, numbers, '-' and '_'." });
    if (await db.Applications.AnyAsync(x => x.Slug == slug, cancellationToken))
        return Results.Conflict(new { error = "An application with this slug already exists." });

    var application = new Application
    {
        Id = Guid.NewGuid(),
        Name = request.Name.Trim(),
        Slug = slug,
        Description = request.Description?.Trim(),
        CreatedAt = DateTime.UtcNow
    };
    db.Applications.Add(application);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/applications/{application.Id}", application);
});

app.MapPost("/api/v1/telemetry/events", async (
    TelemetryBatchRequest request,
    HttpContext httpContext,
    TelemetryIngestionService ingestion,
    CancellationToken cancellationToken) =>
{
    var result = await ingestion.IngestEventsAsync(request.Events, Country(httpContext), cancellationToken);
    return result.Accepted
        ? Results.Accepted()
        : Results.BadRequest(new { error = result.Error });
}).RequireRateLimiting("ingestion");

app.MapPost("/api/v1/telemetry/errors", async (
    ExceptionBatchRequest request,
    HttpContext httpContext,
    TelemetryIngestionService ingestion,
    CancellationToken cancellationToken) =>
{
    var result = await ingestion.IngestErrorsAsync(request.Errors, Country(httpContext), cancellationToken);
    return result.Accepted
        ? Results.Accepted()
        : Results.BadRequest(new { error = result.Error });
}).RequireRateLimiting("ingestion");

app.MapPost("/api/v1/telemetry/installations/heartbeat", async (
    HeartbeatRequest request,
    HttpContext httpContext,
    TelemetryIngestionService ingestion,
    CancellationToken cancellationToken) =>
{
    var result = await ingestion.HeartbeatAsync(request, Country(httpContext), cancellationToken);
    return result.Accepted
        ? Results.Accepted()
        : Results.BadRequest(new { error = result.Error });
}).RequireRateLimiting("ingestion");

app.MapGet("/api/v1/dashboard/overview", async (
    TelemetryDashboardService dashboard,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await dashboard.GetOverviewAsync(cancellationToken));
});

app.MapGet("/api/v1/dashboard/errors", async (
    string? application,
    string? status,
    string? search,
    string? version,
    DateTime? from,
    DateTime? to,
    int? page,
    int? pageSize,
    TelemetryDashboardService dashboard,
    CancellationToken cancellationToken) =>
{
    var result = await dashboard.GetErrorsAsync(
        application,
        status,
        search,
        version,
        from,
        to,
        page ?? 1,
        pageSize ?? 25,
        cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/v1/dashboard/installations", async (
    string? application,
    string? version,
    string? country,
    string? operatingSystem,
    string? architecture,
    string? language,
    int? activeWithinDays,
    int? page,
    int? pageSize,
    TelemetryDashboardService dashboard,
    CancellationToken cancellationToken) =>
{
    var result = await dashboard.GetInstallationsAsync(
        application,
        version,
        country,
        operatingSystem,
        architecture,
        language,
        activeWithinDays ?? 0,
        page ?? 1,
        pageSize ?? 25,
        cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/v1/dashboard/applications/{slug}", async (
    string slug,
    TelemetryDashboardService dashboard,
    CancellationToken cancellationToken) =>
{
    var result = await dashboard.GetApplicationAsync(slug, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapGet("/api/v1/dashboard/applications/{slug}/errors/{errorId:guid}", async (
    string slug,
    Guid errorId,
    TelemetryDashboardService dashboard,
    CancellationToken cancellationToken) =>
{
    var result = await dashboard.GetErrorAsync(slug, errorId, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPost("/api/v1/dashboard/errors/{errorId:guid}/resolve", async (
    Guid errorId,
    ResolveErrorRequest request,
    TelemetryIngestionService ingestion,
    CancellationToken cancellationToken) =>
{
    try
    {
        await ingestion.MarkErrorResolvedAsync(errorId, request.Version, cancellationToken);
        return Results.Ok();
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapPost("/login/submit", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    var returnUrl = DashboardAuthentication.SafeReturnUrl(form["returnUrl"].ToString());

    if (!DashboardAuthentication.PasswordMatches(dashboardPassword, form["password"].ToString()))
        return Results.Redirect(LoginUrl(returnUrl, string.IsNullOrWhiteSpace(dashboardPassword) ? "not-configured" : "invalid"));

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "dashboard")],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    return Results.LocalRedirect(returnUrl);
}).RequireRateLimiting("login");

app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string? Country(HttpContext context)
{
    var value = context.Request.Headers["CF-IPCountry"].FirstOrDefault();
    return string.IsNullOrWhiteSpace(value) || value.Length > 2 ? null : value;
}

static bool IsPublicRequest(PathString path) =>
    path.StartsWithSegments("/api") ||
    path.StartsWithSegments("/health") ||
    path.StartsWithSegments("/login") ||
    path.StartsWithSegments("/logout") ||
    path.StartsWithSegments("/openapi");

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
    context.Response.Redirect(LoginUrl(returnUrl, error));
}

static string LoginUrl(string? returnUrl, string? error = null)
{
    var values = new Dictionary<string, string?>
    {
        ["returnUrl"] = DashboardAuthentication.SafeReturnUrl(returnUrl)
    };
    if (!string.IsNullOrWhiteSpace(error))
        values["error"] = error;
    return QueryHelpers.AddQueryString("/login", values);
}

public sealed record ResolveErrorRequest(string? Version);

public partial class Program;
