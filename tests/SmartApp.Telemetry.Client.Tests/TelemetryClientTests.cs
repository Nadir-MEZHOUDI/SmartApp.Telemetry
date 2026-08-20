using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartApp.Telemetry.Client;
using Xunit;

namespace SmartApp.Telemetry.Client.Tests;

public sealed class TelemetryClientTests
{
    [Fact]
    public async Task Offline_flush_persists_a_bounded_queue()
    {
        var directory = TestDirectory();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTelemetry(options =>
            {
                options.Endpoint = "http://127.0.0.1:1";
                options.Application = "test-app";
                options.StoragePath = directory;
                options.HttpTimeout = TimeSpan.FromMilliseconds(100);
                options.MaxQueueBytes = 4_096;
            });

            await using var provider = services.BuildServiceProvider();
            var telemetry = provider.GetRequiredService<ITelemetryClient>();
            telemetry.TrackAppStarted();
            telemetry.TrackFeatureUsed("ExportPdf");
            await telemetry.FlushAsync();

            var queue = Path.Combine(directory, "telemetry-queue.jsonl");
            Assert.True(File.Exists(queue));
            Assert.InRange(new FileInfo(queue).Length, 1, 4_096);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Disabled_telemetry_does_not_create_events()
    {
        var directory = TestDirectory();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTelemetry(options =>
            {
                options.Endpoint = "http://127.0.0.1:1";
                options.Application = "disabled-app";
                options.StoragePath = directory;
                options.Enabled = false;
            });

            using var provider = services.BuildServiceProvider();
            var telemetry = provider.GetRequiredService<ITelemetryClient>();
            telemetry.Track("app_started");
            telemetry.TrackException(new InvalidOperationException("should not send"));

            Assert.False(File.Exists(Path.Combine(directory, "telemetry-queue.jsonl")));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Flush_sends_events_in_batches_of_max_batch_size()
    {
        using var server = new FakeTelemetryServer();
        var directory = TestDirectory();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTelemetry(options =>
            {
                options.Endpoint = server.BaseUrl;
                options.Application = "test-app";
                options.StoragePath = directory;
                options.MaxBatchSize = 50;
            });

            await using var provider = services.BuildServiceProvider();
            var telemetry = provider.GetRequiredService<ITelemetryClient>();
            for (var index = 0; index < 120; index++)
                telemetry.Track("app_started");
            await telemetry.FlushAsync();

            await server.WaitForRequestsAsync(3, TimeSpan.FromSeconds(5));
            Assert.Equal(3, server.RequestCount);
            Assert.All(server.Bodies, body =>
            {
                using var document = JsonDocument.Parse(body);
                Assert.InRange(document.RootElement.GetProperty("events").GetArrayLength(), 1, 50);
            });
            Assert.False(File.Exists(Path.Combine(directory, "telemetry-queue.jsonl")));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Permanent_client_errors_drop_the_batch_without_requeueing()
    {
        using var server = new FakeTelemetryServer(_ => 400);
        var directory = TestDirectory();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTelemetry(options =>
            {
                options.Endpoint = server.BaseUrl;
                options.Application = "test-app";
                options.StoragePath = directory;
            });

            await using var provider = services.BuildServiceProvider();
            var telemetry = provider.GetRequiredService<ITelemetryClient>();
            telemetry.Track("app_started");
            await telemetry.FlushAsync();

            await server.WaitForRequestsAsync(1, TimeSpan.FromSeconds(5));
            Assert.Equal(1, server.RequestCount);
            Assert.False(File.Exists(Path.Combine(directory, "telemetry-queue.jsonl")));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Transient_failures_are_retried_once_within_a_flush()
    {
        using var server = new FakeTelemetryServer(index => index == 0 ? 429 : 202);
        var directory = TestDirectory();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTelemetry(options =>
            {
                options.Endpoint = server.BaseUrl;
                options.Application = "test-app";
                options.StoragePath = directory;
            });

            await using var provider = services.BuildServiceProvider();
            var telemetry = provider.GetRequiredService<ITelemetryClient>();
            telemetry.Track("app_started");
            await telemetry.FlushAsync();

            await server.WaitForRequestsAsync(2, TimeSpan.FromSeconds(5));
            Assert.Equal(2, server.RequestCount);
            Assert.False(File.Exists(Path.Combine(directory, "telemetry-queue.jsonl")));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Installation_id_is_stable_across_restarts()
    {
        using var server = new FakeTelemetryServer();
        var directory = TestDirectory();
        try
        {
            static Guid TrackOnce(FakeTelemetryServer server, string directory)
            {
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddTelemetry(options =>
                {
                    options.Endpoint = server.BaseUrl;
                    options.Application = "test-app";
                    options.StoragePath = directory;
                });
                using var provider = services.BuildServiceProvider();
                var telemetry = provider.GetRequiredService<ITelemetryClient>();
                telemetry.Track("app_started");
                telemetry.FlushAsync().GetAwaiter().GetResult();
                server.WaitForRequestsAsync(1, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(server.Bodies[0]);
                return document.RootElement.GetProperty("events")[0].GetProperty("installationId").GetGuid();
            }

            var first = TrackOnce(server, directory);
            var second = TrackOnce(server, directory);

            Assert.NotEqual(Guid.Empty, first);
            Assert.Equal(first, second);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Exceptions_are_sent_to_the_error_route()
    {
        using var server = new FakeTelemetryServer();
        var directory = TestDirectory();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTelemetry(options =>
            {
                options.Endpoint = server.BaseUrl;
                options.Application = "test-app";
                options.StoragePath = directory;
            });

            await using var provider = services.BuildServiceProvider();
            var telemetry = provider.GetRequiredService<ITelemetryClient>();
            telemetry.TrackException(new InvalidOperationException("boom"));
            await telemetry.FlushAsync();

            await server.WaitForRequestsAsync(1, TimeSpan.FromSeconds(5));
            Assert.Contains("/api/v1/telemetry/errors", server.Paths[0], StringComparison.Ordinal);
            using var document = JsonDocument.Parse(server.Bodies[0]);
            Assert.Equal("boom", document.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static string TestDirectory() =>
        Path.Combine(Path.GetTempPath(), "smartapp-telemetry-tests", Guid.NewGuid().ToString("N"));

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

internal sealed class FakeTelemetryServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly ConcurrentQueue<(string Path, string Body)> requests = new();
    private readonly Func<int, int> statusHandler;

    public FakeTelemetryServer(Func<int, int>? statusHandler = null)
    {
        this.statusHandler = statusHandler ?? (_ => 202);
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(BaseUrl.TrimEnd('/') + "/");
        listener.Start();
        _ = AcceptLoopAsync();
    }

    public string BaseUrl { get; }

    public int RequestCount => requests.Count;

    public IReadOnlyList<string> Paths => requests.Select(x => x.Path).ToArray();

    public IReadOnlyList<string> Bodies => requests.Select(x => x.Body).ToArray();

    public async Task WaitForRequestsAsync(int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (RequestCount < count && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(RequestCount >= count, $"Expected at least {count} requests, received {RequestCount}.");
    }

    private async Task AcceptLoopAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch { break; }
            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            var index = requests.Count;
            requests.Enqueue((context.Request.Url?.AbsolutePath ?? "", body));
            context.Response.StatusCode = statusHandler(index);
            context.Response.Close();
        }
        catch
        {
            context.Response.Abort();
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose() => listener.Close();
}
