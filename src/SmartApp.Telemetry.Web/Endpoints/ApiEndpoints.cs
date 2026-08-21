using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;
using SmartApp.Telemetry.Infrastructure;
using SmartApp.Telemetry.Web.Services;

namespace SmartApp.Telemetry.Web.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app, string dashboardPassword)
    {
        app.MapGet("/api", () => Results.Ok(new
        {
            service = "SmartApp Telemetry API",
            status = "ok",
            docs = "/openapi/v1.json"
        }));

        app.MapGet("/api/v1/applications", async (IDbContextFactory<TelemetryDbContext> factory, CancellationToken cancellationToken) =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var applications = await db.Applications.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
            var result = applications.Select(x => new { x.Id, x.Name, x.Slug, x.Description, x.IsEnabled, x.CreatedAt });
            return Results.Ok(result);
        });

        app.MapPost("/api/v1/applications", async (
            CreateApplicationRequest request,
            IDbContextFactory<TelemetryDbContext> factory,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
                return Results.BadRequest(new { error = "Name and Slug are required." });

            var slug = request.Slug.Trim().ToLowerInvariant();
            if (slug.Length > 100 || slug.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
                return Results.BadRequest(new { error = "Slug may contain only letters, numbers, '-' and '_'." });
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
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
            var antiforgeryFeature = context.Features.Get<IAntiforgeryValidationFeature>();
            if (antiforgeryFeature is not { IsValid: true })
                return Results.BadRequest(new { error = "Antiforgery token is missing or invalid." });

            var form = await context.Request.ReadFormAsync();
            var returnUrl = DashboardAuthentication.SafeReturnUrl(form["returnUrl"].ToString());

            if (!DashboardAuthentication.PasswordMatches(dashboardPassword, form["password"].ToString()))
                return Results.Redirect(DashboardAuthentication.LoginUrl(
                    returnUrl,
                    string.IsNullOrWhiteSpace(dashboardPassword) ? "not-configured" : "invalid"));

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "dashboard")],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            return Results.LocalRedirect(returnUrl);
        }).WithMetadata(new RequireAntiforgeryTokenAttribute()).RequireRateLimiting("login");

        app.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
    }

    public static string? Country(HttpContext context)
    {
        var value = context.Request.Headers["CF-IPCountry"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) || value.Length > 2 ? null : value;
    }
}
