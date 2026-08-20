namespace SmartApp.Telemetry.Client;

public sealed class TelemetryOptions
{
    public required string Endpoint { get; set; }
    public required string Application { get; set; }
    public string Version { get; set; } = "unknown";
    public bool Enabled { get; set; } = true;
    public bool EnableAnalytics { get; set; } = true;
    public bool EnableCrashReporting { get; set; } = true;
    public string? StoragePath { get; set; }
    public int MaxBatchSize { get; set; } = 50;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(15);
    public int MaxQueueBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxInMemoryItems { get; set; } = 1_000;
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
            throw new ArgumentException("Telemetry Endpoint must be an absolute URL.", nameof(Endpoint));
        if (string.IsNullOrWhiteSpace(Application))
            throw new ArgumentException("Telemetry Application is required.", nameof(Application));
        if (MaxBatchSize is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(MaxBatchSize));
        if (MaxQueueBytes < 1_024) throw new ArgumentOutOfRangeException(nameof(MaxQueueBytes));
        if (MaxInMemoryItems < MaxBatchSize) throw new ArgumentOutOfRangeException(nameof(MaxInMemoryItems));
        if (HeartbeatInterval < TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval));
    }
}
