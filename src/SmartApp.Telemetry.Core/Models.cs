namespace SmartApp.Telemetry.Core;

public sealed class Application
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Installation
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid InstallationId { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string? FirstVersion { get; set; }
    public string? CurrentVersion { get; set; }
    public string? CountryCode { get; set; }
    public string? OperatingSystem { get; set; }
    public string? OperatingSystemVersion { get; set; }
    public string? Architecture { get; set; }
    public string? Language { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TelemetryEvent
{
    public long Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid InstallationId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string PropertiesJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ErrorGroup
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string? FirstSeenVersion { get; set; }
    public string? LastSeenVersion { get; set; }
    public long TotalOccurrences { get; set; }
    public long AffectedInstallations { get; set; }
    public bool IsResolved { get; set; }
    public bool IsRegressed { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedInVersion { get; set; }
}

public sealed class ErrorOccurrence
{
    public long Id { get; set; }
    public Guid ErrorGroupId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid InstallationId { get; set; }
    public string? AppVersion { get; set; }
    public string ExceptionType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string ContextJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; }
}

public sealed class DailyApplicationStat
{
    public Guid ApplicationId { get; set; }
    public DateOnly Date { get; set; }
    public long ActiveInstallations { get; set; }
    public long NewInstallations { get; set; }
    public long TotalEvents { get; set; }
    public long TotalErrors { get; set; }
}

public sealed class DailyEventStat
{
    public Guid ApplicationId { get; set; }
    public DateOnly Date { get; set; }
    public string EventName { get; set; } = string.Empty;
    public long TotalCount { get; set; }
    public long UniqueInstallations { get; set; }
}
