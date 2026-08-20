using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SmartApp.Telemetry.Client;

public sealed class TelemetryClient : ITelemetryClient, IDisposable
{
    private static readonly JsonSerializerOptions PayloadOptions = JsonSerializerOptions.Web;
    private static readonly Action<ILogger, Exception?> WorkerCycleFailed =
        LoggerMessage.Define(LogLevel.Debug, new EventId(1, "WorkerCycleFailed"), "Telemetry worker cycle failed.");

    private static readonly Action<ILogger, Exception?> PayloadSerializationFailed =
        LoggerMessage.Define(LogLevel.Debug, new EventId(2, "PayloadSerializationFailed"), "Telemetry payload could not be serialized.");

    private static readonly Action<ILogger, string?, Exception?> HeartbeatRateLimited =
        LoggerMessage.Define<string?>(LogLevel.Debug, new EventId(3, "HeartbeatRateLimited"), "Heartbeat rate limited; retry after {RetryAfter}.");

    private readonly TelemetryOptions options;
    private readonly HttpClient httpClient;
    private readonly InstallationStore installationStore;
    private readonly LocalQueueStore localQueue;
    private readonly Channel<TelemetryEnvelope> channel;
    private readonly ILogger<TelemetryClient> logger;
    private volatile bool enabled;

    internal TelemetryClient(HttpClient httpClient, TelemetryOptions options, ILogger<TelemetryClient> logger)
    {
        options.Validate();
        this.options = options;
        this.httpClient = httpClient;
        this.logger = logger;
        installationStore = new InstallationStore(options);
        localQueue = new LocalQueueStore(options);
        channel = Channel.CreateBounded<TelemetryEnvelope>(new BoundedChannelOptions(options.MaxInMemoryItems)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        enabled = options.Enabled;
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var envelope in await localQueue.ReadAndClearAsync(cancellationToken))
            channel.Writer.TryWrite(envelope);

        var lastHeartbeat = DateTimeOffset.MinValue;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.FlushInterval, cancellationToken);
                await FlushAsync(cancellationToken);

                if (enabled && options.EnableAnalytics &&
                    DateTimeOffset.UtcNow - lastHeartbeat >= options.HeartbeatInterval)
                {
                    await SendHeartbeatAsync(cancellationToken);
                    lastHeartbeat = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                WorkerCycleFailed(logger, exception);
            }
        }
    }

    public void Track(string eventName) => Track(eventName, null);

    public void Track(string eventName, object? properties)
    {
        if (!enabled || !options.EnableAnalytics) return;
        Enqueue("event", new TelemetryEventRequest(
            options.Application,
            installationStore.InstallationId,
            eventName,
            DateTimeOffset.UtcNow,
            Context(),
            properties is null ? null : ToJson(properties)));
    }

    public void TrackException(Exception exception) => TrackException(exception, null);

    public void TrackException(Exception exception, object? context)
    {
        if (!enabled || !options.EnableCrashReporting) return;
        Enqueue("error", new ExceptionTelemetryRequest(
            options.Application,
            installationStore.InstallationId,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.ToString(),
            DateTimeOffset.UtcNow,
            Context(),
            context is null ? null : ToJson(context)));
    }

    public void TrackAppStarted()
    {
        if (!enabled || !options.EnableAnalytics) return;
        if (!installationStore.FirstStartedSent)
        {
            Track("app_first_started");
            installationStore.MarkFirstStartedSent();
        }
        Track("app_started");
    }

    public void TrackAppClosed()
    {
        if (enabled && options.EnableAnalytics) Track("app_closed");
    }

    public void TrackFeatureUsed(string feature) => Track("feature_used", new { feature });
    public void TrackOperationSucceeded(string operation) => Track("operation_completed", new { operation });

    public void TrackOperationFailed(string operation, Exception? exception = null)
    {
        if (exception is null) Track("operation_failed", new { operation });
        else TrackException(exception, new { operation });
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<TelemetryEnvelope>(options.MaxBatchSize * 2);
        while (channel.Reader.TryRead(out var envelope))
            items.Add(envelope);
        if (items.Count == 0) return;

        foreach (var group in items.GroupBy(x => x.Kind, StringComparer.Ordinal))
        {
            foreach (var batch in group.Chunk(options.MaxBatchSize))
            {
                if (await SendBatchAsync(group.Key, batch, cancellationToken) == SendResult.RetryLater)
                    await localQueue.AppendAsync(batch, cancellationToken);
            }
        }
    }

    public void SetEnabled(bool enabled) => this.enabled = enabled;

    private async Task<SendResult> SendBatchAsync(string kind, IReadOnlyCollection<TelemetryEnvelope> batch, CancellationToken cancellationToken)
    {
        var payloads = batch.Select(x => x.Payload).ToArray();
        object body = kind == "error" ? new { errors = payloads } : new { events = payloads };
        var route = kind == "error" ? "api/v1/telemetry/errors" : "api/v1/telemetry/events";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var response = await httpClient.PostAsJsonAsync(route, body, cancellationToken);
                if (response.IsSuccessStatusCode) return SendResult.Sent;
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                    continue;
                if ((int)response.StatusCode < 500) return SendResult.Drop;
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
        }

        return SendResult.RetryLater;
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var payload = new
        {
            application = options.Application,
            installationId = installationStore.InstallationId,
            timestamp = DateTimeOffset.UtcNow,
            context = Context()
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/v1/telemetry/installations/heartbeat", payload, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retryAfter)
                HeartbeatRateLimited(logger, retryAfter.ToString(), null);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    private enum SendResult
    {
        Sent,
        Drop,
        RetryLater
    }

    private void Enqueue<T>(string kind, T payload)
    {
        try
        {
            var json = JsonSerializer.SerializeToElement(payload, PayloadOptions);
            channel.Writer.TryWrite(new TelemetryEnvelope(kind, json));
        }
        catch (Exception exception)
        {
            PayloadSerializationFailed(logger, exception);
        }
    }

    public void Dispose() => localQueue.Dispose();

    private TelemetryContext Context() => new(
        options.Version,
        Environment.OSVersion.Platform.ToString(),
        Environment.OSVersion.VersionString,
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        System.Globalization.CultureInfo.CurrentUICulture.Name);

    private static JsonElement ToJson(object value) => JsonSerializer.SerializeToElement(value);
}
