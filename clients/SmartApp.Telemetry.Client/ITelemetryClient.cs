namespace SmartApp.Telemetry.Client;

public interface ITelemetryClient
{
    Guid InstallationId { get; }
    void Track(string eventName);
    void Track(string eventName, object? properties);
    void TrackException(Exception exception);
    void TrackException(Exception exception, object? context);
    void TrackAppStarted();
    void TrackAppClosed();
    void TrackFeatureUsed(string feature);
    void TrackOperationSucceeded(string operation);
    void TrackOperationFailed(string operation, Exception? exception = null);
    Task FlushAsync(CancellationToken cancellationToken = default);
    void SetEnabled(bool enabled);
}
