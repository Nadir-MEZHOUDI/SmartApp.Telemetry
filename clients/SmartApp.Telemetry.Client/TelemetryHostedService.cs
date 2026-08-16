using Microsoft.Extensions.Hosting;

namespace SmartApp.Telemetry.Client;

internal sealed class TelemetryHostedService(TelemetryClient client) : IHostedService
{
    private CancellationTokenSource? cancellation;
    private Task? worker;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        worker = client.RunAsync(cancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (cancellation is null) return;
        await cancellation.CancelAsync();
        if (worker is not null)
        {
            try { await worker.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        await client.FlushAsync(cancellationToken);
        cancellation.Dispose();
    }
}
