using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;
using SmartApp.Telemetry.Infrastructure;

namespace SmartApp.Telemetry.Web.Services;

public sealed class TelemetryApiClient(
    IDbContextFactory<TelemetryDbContext> factory,
    TelemetryDashboardService dashboard,
    TelemetryIngestionService ingestion)
{
    public async Task<IReadOnlyList<ApplicationListItem>> GetApplicationsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var applications = await db.Applications.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ApplicationListItem(x.Id, x.Name, x.Slug, x.Description, x.IsEnabled, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return applications;
    }

    public Task<DashboardOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        dashboard.GetOverviewAsync(cancellationToken);

    public Task<DashboardApplication?> GetApplicationAsync(string slug, CancellationToken cancellationToken = default) =>
        dashboard.GetApplicationAsync(slug, cancellationToken);

    public Task<DashboardErrorPage> GetErrorsAsync(ErrorFilters filters, int page, CancellationToken cancellationToken = default) =>
        dashboard.GetErrorsAsync(
            EmptyToNull(filters.Application),
            EmptyToNull(filters.Status),
            EmptyToNull(filters.Search),
            EmptyToNull(filters.Version),
            ParseDate(filters.From),
            ParseDate(filters.To),
            page,
            25,
            cancellationToken);

    public Task<DashboardInstallationPage> GetInstallationsAsync(InstallationFilters filters, int page, CancellationToken cancellationToken = default) =>
        dashboard.GetInstallationsAsync(
            EmptyToNull(filters.Application),
            EmptyToNull(filters.Version),
            EmptyToNull(filters.Country),
            EmptyToNull(filters.OperatingSystem),
            EmptyToNull(filters.Architecture),
            EmptyToNull(filters.Language),
            filters.ActiveWithinDays,
            page,
            25,
            cancellationToken);

    public Task<ErrorDetails?> GetErrorAsync(string slug, Guid errorId, CancellationToken cancellationToken = default) =>
        dashboard.GetErrorAsync(slug, errorId, cancellationToken);

    public Task ResolveErrorAsync(Guid errorId, string? version, CancellationToken cancellationToken = default) =>
        ingestion.MarkErrorResolvedAsync(errorId, version, cancellationToken);

    public async Task<Application> CreateApplicationAsync(string name, string slug, string? description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Name and Slug are required.");
        var normalized = slug.Trim().ToLowerInvariant();
        if (normalized.Length > 100 || normalized.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Slug may contain only letters, numbers, '-' and '_'.");

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (await db.Applications.AnyAsync(x => x.Slug == normalized, cancellationToken))
            throw new InvalidOperationException("An application with this slug already exists.");

        var app = new Application
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = normalized,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Applications.Add(app);
        await db.SaveChangesAsync(cancellationToken);
        return app;
    }

    public async Task<Application> UpdateApplicationAsync(string slug, string name, string? description, bool isEnabled, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.");
        var normalized = slug.Trim().ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var app = await db.Applications.SingleOrDefaultAsync(x => x.Slug == normalized, cancellationToken)
                  ?? throw new KeyNotFoundException("Application not found.");
        app.Name = name.Trim();
        app.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        app.IsEnabled = isEnabled;
        await db.SaveChangesAsync(cancellationToken);
        return app;
    }

    public async Task DeleteApplicationAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var app = await db.Applications.SingleOrDefaultAsync(x => x.Slug == normalized, cancellationToken)
                  ?? throw new KeyNotFoundException("Application not found.");
        var appId = app.Id;
        if (db.Database.IsRelational())
        {
            await db.ErrorOccurrences.Where(x => x.ApplicationId == appId).ExecuteDeleteAsync(cancellationToken);
            await db.ErrorGroups.Where(x => x.ApplicationId == appId).ExecuteDeleteAsync(cancellationToken);
            await db.TelemetryEvents.Where(x => x.ApplicationId == appId).ExecuteDeleteAsync(cancellationToken);
            await db.Installations.Where(x => x.ApplicationId == appId).ExecuteDeleteAsync(cancellationToken);
            await db.DailyEventStats.Where(x => x.ApplicationId == appId).ExecuteDeleteAsync(cancellationToken);
            await db.DailyApplicationStats.Where(x => x.ApplicationId == appId).ExecuteDeleteAsync(cancellationToken);
            await db.Applications.Where(x => x.Id == appId).ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var occ = await db.ErrorOccurrences.Where(x => x.ApplicationId == appId).ToListAsync(cancellationToken);
            db.ErrorOccurrences.RemoveRange(occ);
            var groups = await db.ErrorGroups.Where(x => x.ApplicationId == appId).ToListAsync(cancellationToken);
            db.ErrorGroups.RemoveRange(groups);
            var evts = await db.TelemetryEvents.Where(x => x.ApplicationId == appId).ToListAsync(cancellationToken);
            db.TelemetryEvents.RemoveRange(evts);
            var insts = await db.Installations.Where(x => x.ApplicationId == appId).ToListAsync(cancellationToken);
            db.Installations.RemoveRange(insts);
            var des = await db.DailyEventStats.Where(x => x.ApplicationId == appId).ToListAsync(cancellationToken);
            db.DailyEventStats.RemoveRange(des);
            var das = await db.DailyApplicationStats.Where(x => x.ApplicationId == appId).ToListAsync(cancellationToken);
            db.DailyApplicationStats.RemoveRange(das);
            db.Applications.Remove(app);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateTime? ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
