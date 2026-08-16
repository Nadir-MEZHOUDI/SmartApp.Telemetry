using Microsoft.Extensions.DependencyInjection;
using SmartApp.Telemetry.Client;
using Xunit;

namespace SmartApp.Telemetry.Client.Tests;

public sealed class TelemetryClientTests
{
    [Fact]
    public async Task Offline_flush_persists_a_bounded_queue()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smartapp-telemetry-tests", Guid.NewGuid().ToString("N"));
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
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Disabled_telemetry_does_not_create_events()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smartapp-telemetry-tests", Guid.NewGuid().ToString("N"));
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
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
