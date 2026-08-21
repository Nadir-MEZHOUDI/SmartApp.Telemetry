using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;

namespace SmartApp.Telemetry.Infrastructure;

public sealed class TelemetryDashboardService(IDbContextFactory<TelemetryDbContext> factory)
{
    public async Task<DashboardOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var today = now.Date;
        var apps = await db.Applications.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

        var stats = await db.Installations.AsNoTracking()
            .GroupBy(x => x.ApplicationId)
            .Select(group => new
            {
                ApplicationId = group.Key,
                Total = group.LongCount(),
                ActiveToday = group.LongCount(x => x.LastSeenAt >= today),
                Active7 = group.LongCount(x => x.LastSeenAt >= now.AddDays(-7)),
                Active30 = group.LongCount(x => x.LastSeenAt >= now.AddDays(-30))
            })
            .ToDictionaryAsync(x => x.ApplicationId, cancellationToken);

        var applicationSummaries = apps.Select(app => stats.TryGetValue(app.Id, out var appStats)
            ? new ApplicationSummary(app.Id, app.Name, app.Slug, app.IsEnabled, appStats.Total, appStats.ActiveToday, appStats.Active7, appStats.Active30)
            : new ApplicationSummary(app.Id, app.Name, app.Slug, app.IsEnabled, 0, 0, 0, 0)).ToList();

        var eventsToday = await db.TelemetryEvents.LongCountAsync(x => x.OccurredAt >= today, cancellationToken);
        var errorsToday = await db.ErrorOccurrences.LongCountAsync(x => x.OccurredAt >= today, cancellationToken);
        var errorInstallationsToday = await db.ErrorOccurrences
            .Where(x => x.OccurredAt >= today)
            .Select(x => x.InstallationId)
            .Distinct()
            .LongCountAsync(cancellationToken);

        return new DashboardOverview(
            applicationSummaries.Sum(x => x.Installations),
            applicationSummaries.Sum(x => x.ActiveToday),
            applicationSummaries.Sum(x => x.Active7Days),
            applicationSummaries.Sum(x => x.Active30Days),
            eventsToday,
            errorsToday,
            applicationSummaries.Sum(x => x.Installations) - errorInstallationsToday,
            applicationSummaries);
    }

    public async Task<DashboardApplication?> GetApplicationAsync(string slug, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(
            x => x.Slug == normalizedSlug,
            cancellationToken);
        if (application is null) return null;

        var now = DateTime.UtcNow;
        var summary = await BuildSummaryAsync(db, application, now, cancellationToken);

        var activityRows = await db.TelemetryEvents.AsNoTracking()
            .Where(x => x.ApplicationId == application.Id && x.OccurredAt >= now.AddDays(-30))
            .GroupBy(x => x.OccurredAt.Date)
            .Select(group => new
            {
                Date = group.Key,
                Installations = group.Select(x => x.InstallationId).Distinct().LongCount()
            })
            .ToListAsync(cancellationToken);
        var activity = activityRows
            .OrderBy(x => x.Date)
            .Select(x => new ChartPoint(x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Installations))
            .ToList();

        var versions = await CountGroupedAsync(db, application.Id, x => x.CurrentVersion, cancellationToken);
        var countries = await CountGroupedAsync(db, application.Id, x => x.CountryCode, cancellationToken);
        var operatingSystems = await CountGroupedAsync(db, application.Id, x => x.OperatingSystem, cancellationToken);
        var architectures = await CountGroupedAsync(db, application.Id, x => x.Architecture, cancellationToken);
        var languages = await CountGroupedAsync(db, application.Id, x => x.Language, cancellationToken);

        var featureEvents = await db.TelemetryEvents.AsNoTracking()
            .Where(x => x.ApplicationId == application.Id && x.EventName == "feature_used" && x.OccurredAt >= now.AddDays(-90))
            .Select(x => x.PropertiesJson)
            .ToListAsync(cancellationToken);
        var features = featureEvents
            .Select(ReadFeature)
            .Where(x => x is not null)
            .GroupBy(x => x!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CountItem(group.Key, group.LongCount()))
            .OrderByDescending(x => x.Count)
            .Take(20)
            .ToList();

        var recentErrors = await db.ErrorGroups.AsNoTracking()
            .Where(x => x.ApplicationId == application.Id)
            .OrderByDescending(x => x.LastSeenAt)
            .Take(20)
            .Select(x => ToErrorListItem(x))
            .ToListAsync(cancellationToken);

        return new DashboardApplication(summary, activity, versions, countries, operatingSystems, architectures, languages, features, recentErrors);
    }

    public async Task<ErrorDetails?> GetErrorAsync(string slug, Guid errorId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var group = await db.ErrorGroups.AsNoTracking()
            .Join(db.Applications, error => error.ApplicationId, app => app.Id, (error, app) => new { error, app.Slug })
            .Where(x => x.Slug == normalizedSlug && x.error.Id == errorId)
            .Select(x => x.error)
            .SingleOrDefaultAsync(cancellationToken);
        if (group is null) return null;

        var occurrences = await db.ErrorOccurrences.AsNoTracking()
            .Where(x => x.ErrorGroupId == group.Id)
            .OrderByDescending(x => x.OccurredAt)
            .Take(50)
            .Select(x => new ErrorOccurrenceView(x.InstallationId, x.AppVersion, x.Message, x.StackTrace, x.OccurredAt, x.ContextJson))
            .ToListAsync(cancellationToken);

        return new ErrorDetails(
            group.Id,
            group.Title,
            group.ExceptionType,
            group.TotalOccurrences,
            group.AffectedInstallations,
            group.FirstSeenAt,
            group.LastSeenAt,
            group.FirstSeenVersion,
            group.LastSeenVersion,
            group.IsResolved,
            group.IsRegressed,
            group.ResolvedAt,
            group.ResolvedInVersion,
            occurrences);
    }

    public async Task<DashboardErrorPage> GetErrorsAsync(
        string? applicationSlug,
        string? status,
        string? search,
        string? version,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.ErrorGroups.AsNoTracking()
            .Join(db.Applications.AsNoTracking(), error => error.ApplicationId, application => application.Id,
                (error, application) => new { Error = error, Application = application });

        if (!string.IsNullOrWhiteSpace(applicationSlug))
        {
            var normalizedSlug = applicationSlug.Trim().ToLowerInvariant();
            query = query.Where(x => x.Application.Slug == normalizedSlug);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Error.Title.Contains(term) || x.Error.ExceptionType.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(version))
        {
            var requestedVersion = version.Trim();
            query = query.Where(x => x.Error.FirstSeenVersion == requestedVersion || x.Error.LastSeenVersion == requestedVersion);
        }
        if (from.HasValue)
            query = query.Where(x => x.Error.LastSeenAt >= from.Value.Date);
        if (to.HasValue)
            query = query.Where(x => x.Error.LastSeenAt < to.Value.Date.AddDays(1));

        switch (status?.Trim().ToLowerInvariant())
        {
            case "open":
                query = query.Where(x => !x.Error.IsResolved && !x.Error.IsRegressed);
                break;
            case "resolved":
                query = query.Where(x => x.Error.IsResolved);
                break;
            case "regressed":
                query = query.Where(x => x.Error.IsRegressed);
                break;
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.Error.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new DashboardErrorRow(
            x.Error.Id,
            x.Application.Name,
            x.Application.Slug,
            x.Error.Title,
            x.Error.ExceptionType,
            x.Error.TotalOccurrences,
            x.Error.AffectedInstallations,
            x.Error.FirstSeenAt,
            x.Error.LastSeenAt,
            x.Error.FirstSeenVersion,
            x.Error.LastSeenVersion,
            StatusOf(x.Error))).ToList();

        return new DashboardErrorPage(total, page, pageSize, items);
    }

    public async Task<DashboardInstallationPage> GetInstallationsAsync(
        string? applicationSlug,
        string? version,
        string? country,
        string? operatingSystem,
        string? architecture,
        string? language,
        int activeWithinDays,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Installations.AsNoTracking()
            .Join(db.Applications.AsNoTracking(), installation => installation.ApplicationId, application => application.Id,
                (installation, application) => new { Installation = installation, Application = application });

        if (!string.IsNullOrWhiteSpace(applicationSlug))
        {
            var normalizedSlug = applicationSlug.Trim().ToLowerInvariant();
            query = query.Where(x => x.Application.Slug == normalizedSlug);
        }
        if (!string.IsNullOrWhiteSpace(version))
            query = query.Where(x => x.Installation.CurrentVersion == version.Trim());
        if (!string.IsNullOrWhiteSpace(country))
        {
            var normalizedCountry = country.Trim().ToUpperInvariant();
            query = query.Where(x => x.Installation.CountryCode == normalizedCountry);
        }
        if (!string.IsNullOrWhiteSpace(operatingSystem))
            query = query.Where(x => x.Installation.OperatingSystem == operatingSystem.Trim());
        if (!string.IsNullOrWhiteSpace(architecture))
            query = query.Where(x => x.Installation.Architecture == architecture.Trim());
        if (!string.IsNullOrWhiteSpace(language))
            query = query.Where(x => x.Installation.Language == language.Trim());
        if (activeWithinDays > 0)
            query = query.Where(x => x.Installation.LastSeenAt >= DateTime.UtcNow.AddDays(-activeWithinDays));

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.Installation.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new DashboardInstallationRow(
            x.Installation.InstallationId,
            x.Application.Name,
            x.Application.Slug,
            x.Installation.CurrentVersion,
            x.Installation.CountryCode,
            x.Installation.OperatingSystem,
            x.Installation.Architecture,
            x.Installation.Language,
            x.Installation.FirstSeenAt,
            x.Installation.LastSeenAt)).ToList();

        return new DashboardInstallationPage(total, page, pageSize, items);
    }

    private static async Task<ApplicationSummary> BuildSummaryAsync(TelemetryDbContext db, Application application, DateTime now, CancellationToken cancellationToken)
    {
        var today = now.Date;
        var stats = await db.Installations.AsNoTracking()
            .Where(x => x.ApplicationId == application.Id)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.LongCount(),
                ActiveToday = group.LongCount(x => x.LastSeenAt >= today),
                Active7 = group.LongCount(x => x.LastSeenAt >= now.AddDays(-7)),
                Active30 = group.LongCount(x => x.LastSeenAt >= now.AddDays(-30))
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new ApplicationSummary(
            application.Id,
            application.Name,
            application.Slug,
            application.IsEnabled,
            stats?.Total ?? 0,
            stats?.ActiveToday ?? 0,
            stats?.Active7 ?? 0,
            stats?.Active30 ?? 0);
    }

    private static async Task<List<CountItem>> CountGroupedAsync(
        TelemetryDbContext db,
        Guid applicationId,
        Expression<Func<Installation, string?>> selector,
        CancellationToken cancellationToken)
    {
        var rows = await db.Installations.AsNoTracking()
            .Where(x => x.ApplicationId == applicationId)
            .GroupBy(selector)
            .Select(group => new { Key = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        return rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CountItem(group.Key, group.Sum(x => x.Count)))
            .OrderByDescending(x => x.Count)
            .ToList();
    }

    private static string? ReadFeature(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("feature", out var feature) ? feature.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ErrorListItem ToErrorListItem(ErrorGroup group)
    {
        var status = StatusOf(group);
        return new ErrorListItem(group.Id, group.Title, group.ExceptionType, group.TotalOccurrences, group.AffectedInstallations, group.FirstSeenAt, group.LastSeenAt, group.FirstSeenVersion, group.LastSeenVersion, status);
    }

    private static string StatusOf(ErrorGroup group) =>
        group.IsRegressed ? "Regressed" : group.IsResolved ? "Resolved" : "Open";
}
