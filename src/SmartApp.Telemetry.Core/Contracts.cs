using System.Text.Json;

namespace SmartApp.Telemetry.Core;

public sealed record CreateApplicationRequest(string Name, string Slug, string? Description);

public sealed record UpdateApplicationRequest(string Name, string? Description, bool? IsEnabled);

public sealed record TelemetryContext(
    string? AppVersion,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    string? Architecture,
    string? Language);

public sealed record TelemetryEventRequest(
    string Application,
    Guid InstallationId,
    string EventName,
    DateTimeOffset Timestamp,
    TelemetryContext? Context,
    JsonElement? Properties);

public sealed record TelemetryBatchRequest(IReadOnlyList<TelemetryEventRequest> Events);

public sealed record ExceptionTelemetryRequest(
    string Application,
    Guid InstallationId,
    string ExceptionType,
    string? Message,
    string? StackTrace,
    DateTimeOffset Timestamp,
    TelemetryContext? Context,
    JsonElement? AdditionalContext);

public sealed record ExceptionBatchRequest(IReadOnlyList<ExceptionTelemetryRequest> Errors);

public sealed record HeartbeatRequest(
    string Application,
    Guid InstallationId,
    DateTimeOffset Timestamp,
    TelemetryContext? Context);

public sealed record ApplicationSummary(
    Guid Id,
    string Name,
    string Slug,
    bool IsEnabled,
    long Installations,
    long ActiveToday,
    long Active7Days,
    long Active30Days);

public sealed record DashboardOverview(
    long TotalInstallations,
    long ActiveToday,
    long Active7Days,
    long Active30Days,
    long EventsToday,
    long ErrorsToday,
    long CrashFreeInstallations,
    IReadOnlyList<ApplicationSummary> Applications);

public sealed record DashboardApplication(
    ApplicationSummary Summary,
    IReadOnlyList<ChartPoint> Activity,
    IReadOnlyList<CountItem> Versions,
    IReadOnlyList<CountItem> Countries,
    IReadOnlyList<CountItem> OperatingSystems,
    IReadOnlyList<CountItem> Architectures,
    IReadOnlyList<CountItem> Languages,
    IReadOnlyList<CountItem> Features,
    IReadOnlyList<ErrorListItem> RecentErrors);

public sealed record ChartPoint(string Date, long Value);
public sealed record CountItem(string Name, long Count);
public sealed record ErrorListItem(Guid Id, string Title, string ExceptionType, long Occurrences, long AffectedInstallations, DateTime FirstSeenAt, DateTime LastSeenAt, string? FirstSeenVersion, string? LastSeenVersion, string Status);

public sealed record ErrorDetails(
    Guid Id,
    string Title,
    string ExceptionType,
    long Occurrences,
    long AffectedInstallations,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    string? FirstSeenVersion,
    string? LastSeenVersion,
    bool IsResolved,
    bool IsRegressed,
    DateTime? ResolvedAt,
    string? ResolvedInVersion,
    IReadOnlyList<ErrorOccurrenceView> RecentOccurrences);

public sealed record ErrorOccurrenceView(Guid InstallationId, string? AppVersion, string Message, string? StackTrace, DateTime OccurredAt, string ContextJson);

public sealed record DashboardErrorRow(
    Guid Id,
    string Application,
    string ApplicationSlug,
    string Title,
    string ExceptionType,
    long Occurrences,
    long AffectedInstallations,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    string? FirstSeenVersion,
    string? LastSeenVersion,
    string Status);

public sealed record DashboardErrorPage(long Total, int Page, int PageSize, IReadOnlyList<DashboardErrorRow> Items);

public sealed record DashboardInstallationRow(
    Guid InstallationId,
    string Application,
    string ApplicationSlug,
    string? CurrentVersion,
    string? CountryCode,
    string? OperatingSystem,
    string? Architecture,
    string? Language,
    DateTime FirstSeenAt,
    DateTime LastSeenAt);

public sealed record DashboardInstallationPage(
    long Total,
    int Page,
    int PageSize,
    IReadOnlyList<DashboardInstallationRow> Items);
