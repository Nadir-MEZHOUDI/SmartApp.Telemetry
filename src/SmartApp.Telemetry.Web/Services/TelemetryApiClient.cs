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

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateTime? ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
