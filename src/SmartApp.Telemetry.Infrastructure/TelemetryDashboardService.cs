using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartApp.Telemetry.Core;

namespace SmartApp.Telemetry.Infrastructure;

public sealed class TelemetryDashboardService(TelemetryDbContext db)
{
    public async Task<DashboardOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var apps = await db.Applications.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var applicationSummaries = new List<ApplicationSummary>();

        foreach (var app in apps)
            applicationSummaries.Add(await BuildSummaryAsync(app, now, cancellationToken));

        var totalInstallations = await db.Installations.LongCountAsync(cancellationToken);
        var activeToday = await db.Installations.LongCountAsync(x => x.LastSeenAt >= today, cancellationToken);
        var active7 = await db.Installations.LongCountAsync(x => x.LastSeenAt >= now.AddDays(-7), cancellationToken);
        var active30 = await db.Installations.LongCountAsync(x => x.LastSeenAt >= now.AddDays(-30), cancellationToken);
        var eventsToday = await db.TelemetryEvents.LongCountAsync(x => x.OccurredAt >= today, cancellationToken);
        var errorsToday = await db.ErrorOccurrences.LongCountAsync(x => x.OccurredAt >= today, cancellationToken);
        var errorInstallationsToday = await db.ErrorOccurrences
            .Where(x => x.OccurredAt >= today)
            .Select(x => x.InstallationId)
            .Distinct()
            .LongCountAsync(cancellationToken);

        return new DashboardOverview(
            totalInstallations,
            activeToday,
            active7,
            active30,
            eventsToday,
            errorsToday,
            totalInstallations - errorInstallationsToday,
            applicationSummaries);
    }

    public async Task<DashboardApplication?> GetApplicationAsync(string slug, CancellationToken cancellationToken)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(
            x => x.Slug == normalizedSlug,
            cancellationToken);
        if (application is null) return null;

        var now = DateTime.UtcNow;
        var summary = await BuildSummaryAsync(application, now, cancellationToken);
        var installations = await db.Installations.AsNoTracking()
            .Where(x => x.ApplicationId == application.Id)
            .ToListAsync(cancellationToken);

        var activityEvents = await db.TelemetryEvents.AsNoTracking()
            .Where(x => x.ApplicationId == application.Id && x.OccurredAt >= now.AddDays(-30))
            .Select(x => new { x.InstallationId, x.OccurredAt })
            .ToListAsync(cancellationToken);
        var activity = activityEvents
            .GroupBy(x => x.OccurredAt.Date)
            .Select(group => new ChartPoint(group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), group.Select(x => x.InstallationId).Distinct().LongCount()))
            .OrderBy(x => x.Date)
            .ToList();

        var versions = Count(installations.Select(x => x.CurrentVersion));
        var countries = Count(installations.Select(x => x.CountryCode));
        var operatingSystems = Count(installations.Select(x => x.OperatingSystem));
        var architectures = Count(installations.Select(x => x.Architecture));
        var languages = Count(installations.Select(x => x.Language));

        var featureEvents = await db.TelemetryEvents.AsNoTracking()
            .Where(x => x.ApplicationId == application.Id && x.EventName == "feature_used")
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

    private async Task<ApplicationSummary> BuildSummaryAsync(Application application, DateTime now, CancellationToken cancellationToken)
    {
        var today = now.Date;
        var installations = db.Installations.Where(x => x.ApplicationId == application.Id);
        return new ApplicationSummary(
            application.Id,
            application.Name,
            application.Slug,
            application.IsEnabled,
            await installations.LongCountAsync(cancellationToken),
            await installations.LongCountAsync(x => x.LastSeenAt >= today, cancellationToken),
            await installations.LongCountAsync(x => x.LastSeenAt >= now.AddDays(-7), cancellationToken),
            await installations.LongCountAsync(x => x.LastSeenAt >= now.AddDays(-30), cancellationToken));
    }

    private static List<CountItem> Count(IEnumerable<string?> values) =>
        values.Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CountItem(group.Key, group.LongCount()))
            .OrderByDescending(x => x.Count)
            .ToList();

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
