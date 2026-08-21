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

    // Verbose bridge for WPF sample — mirrors every HTTP result as a multiline log line
    private static readonly Action<ILogger, string, int, string?, Exception?> IngestResult =
        LoggerMessage.Define<string, int, string?>(LogLevel.Information, new EventId(10, "IngestResult"), "Telemetry ingest {Route} -> {Status} {Body}");
    private static readonly Action<ILogger, string, string?, Exception?> IngestException =
        LoggerMessage.Define<string, string?>(LogLevel.Warning, new EventId(11, "IngestException"), "Telemetry ingest {Route} exception: {Message}");
    private static readonly Action<ILogger, string, Exception?> EnqueueFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(12, "EnqueueFailed"), "Telemetry enqueue failed for {Kind}");

    private readonly TelemetryOptions options;
    private readonly HttpClient httpClient;
    private readonly InstallationStore installationStore;
    private readonly LocalQueueStore localQueue;
    private readonly Channel<TelemetryEnvelope> channel;
    private readonly ILogger<TelemetryClient> logger;
    private volatile bool enabled;

    public Guid InstallationId => installationStore.InstallationId;

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
                WpfLogBridge.Write($"[WORKER] cycle failed: {exception.GetType().Name}: {exception.Message}\n{exception}");
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
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var truncated = responseBody.Length > 1200 ? responseBody[..1200] + " …(truncated)" : responseBody;
                var status = (int)response.StatusCode;
                var line = $"[RESPONSE] POST {route} -> {status} {response.StatusCode}  attempt {attempt + 1}/2  batch={batch.Count}  body={(string.IsNullOrWhiteSpace(truncated) ? "(empty — 202 Accepted expected)" : truncated)}";
                IngestResult(logger, route, status, truncated, null);
                WpfLogBridge.Write(line);

                if (response.IsSuccessStatusCode) return SendResult.Sent;
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                {
                    WpfLogBridge.Write($"[RETRY] {route} rate-limited/timeout, will retry...");
                    continue;
                }
                if (status < 500)
                {
                    WpfLogBridge.Write($"[DROP] {route} {status} <500 — server rejected payload (check app slug / validation). Not retried. Body: {truncated}");
                    return SendResult.Drop;
                }
            }
            catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var msg = $"[EXCEPTION] POST {route} HttpRequestException attempt {attempt + 1}/2: {ex.Message} {(ex.InnerException is not null ? $" Inner: {ex.InnerException.Message}" : "")}";
                IngestException(logger, route, ex.Message, ex);
                WpfLogBridge.Write(msg);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var msg = $"[TIMEOUT] POST {route} TaskCanceledException attempt {attempt + 1}/2 (HttpTimeout={options.HttpTimeout.TotalSeconds}s): {ex.Message}";
                IngestException(logger, route, ex.Message, ex);
                WpfLogBridge.Write(msg);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                WpfLogBridge.Write($"[EXCEPTION] POST {route} {ex.GetType().Name}: {ex.Message}\n{ex}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
        }

        WpfLogBridge.Write($"[RETRY-LATER] {route} batch={batch.Count} queued to local file — will retry next flush.");
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
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            WpfLogBridge.Write($"[HEARTBEAT] POST heartbeat -> {(int)response.StatusCode} {response.StatusCode} body={(string.IsNullOrWhiteSpace(body) ? "(empty)" : body[..Math.Min(600, body.Length)])}");
            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retryAfter)
                HeartbeatRateLimited(logger, retryAfter.ToString(), null);
        }
        catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
        {
            WpfLogBridge.Write($"[HEARTBEAT EXCEPTION] HttpRequestException: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            WpfLogBridge.Write($"[HEARTBEAT TIMEOUT] {ex.Message}");
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
            var ok = channel.Writer.TryWrite(new TelemetryEnvelope(kind, json));
            if (!ok) WpfLogBridge.Write($"[ENQUEUE] channel full (MaxInMemoryItems={options.MaxInMemoryItems}) — oldest dropped. kind={kind}");
            else WpfLogBridge.Write($"[ENQUEUE] {kind} queued — channel count grows. payload={json.GetRawText()[..Math.Min(400, json.GetRawText().Length)]}");
        }
        catch (Exception exception)
        {
            PayloadSerializationFailed(logger, exception);
            EnqueueFailed(logger, kind, exception);
            WpfLogBridge.Write($"[ENQUEUE FAILED] kind={kind} {exception.GetType().Name}: {exception.Message}\n{exception}");
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
