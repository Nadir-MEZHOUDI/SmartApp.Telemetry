namespace SmartApp.Telemetry.Client;

public sealed class TelemetrySession : ITelemetryClient, IAsyncDisposable
{
    private readonly TelemetryClient client;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private bool disposed;

    internal TelemetrySession(TelemetryClient client)
    {
        this.client = client;
        worker = Task.Run(() => client.RunAsync(cancellation.Token));
    }

    public void Track(string eventName) => client.Track(eventName);

    public void Track(string eventName, object? properties) => client.Track(eventName, properties);

    public void TrackException(Exception exception) => client.TrackException(exception);

    public void TrackException(Exception exception, object? context) => client.TrackException(exception, context);

    public void TrackAppStarted() => client.TrackAppStarted();

    public void TrackAppClosed() => client.TrackAppClosed();

    public void TrackFeatureUsed(string feature) => client.TrackFeatureUsed(feature);

    public void TrackOperationSucceeded(string operation) => client.TrackOperationSucceeded(operation);

    public void TrackOperationFailed(string operation, Exception? exception = null) => client.TrackOperationFailed(operation, exception);

    public Task FlushAsync(CancellationToken cancellationToken = default) => client.FlushAsync(cancellationToken);

    public void SetEnabled(bool enabled) => client.SetEnabled(enabled);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        await cancellation.CancelAsync();
        try
        {
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }

        await client.FlushAsync();
        client.Dispose();
        cancellation.Dispose();
    }
}
