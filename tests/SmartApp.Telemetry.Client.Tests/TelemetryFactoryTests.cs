using System.Text.Json;
using SmartApp.Telemetry.Client;
using Xunit;

namespace SmartApp.Telemetry.Client.Tests;

public sealed class TelemetryFactoryTests
{
    [Fact]
    public async Task Factory_create_sends_events_without_a_di_container()
    {
        using var server = new FakeTelemetryServer();
        var directory = TestDirectory();
        try
        {
            await using var session = TelemetryFactory.Create(options =>
            {
                options.Endpoint = server.BaseUrl;
                options.Application = "test-app";
                options.StoragePath = directory;
            });

            session.TrackAppStarted();
            await session.FlushAsync();

            await server.WaitForRequestsAsync(1, TimeSpan.FromSeconds(5));
            Assert.Contains("/api/v1/telemetry/events", server.Paths[0], StringComparison.Ordinal);
            using var document = JsonDocument.Parse(server.Bodies[0]);
            Assert.Equal("app_first_started", document.RootElement.GetProperty("events")[0].GetProperty("eventName").GetString());
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Factory_create_rejects_invalid_options()
    {
        Assert.Throws<ArgumentException>(() =>
            TelemetryFactory.Create(options => options.Application = ""));
    }

    [Fact]
    public async Task Factory_session_dispose_flushes_remaining_events()
    {
        using var server = new FakeTelemetryServer();
        var directory = TestDirectory();
        try
        {
            var session = TelemetryFactory.Create(options =>
            {
                options.Endpoint = server.BaseUrl;
                options.Application = "test-app";
                options.StoragePath = directory;
            });

            session.Track("app_started");
            await session.DisposeAsync();

            await server.WaitForRequestsAsync(1, TimeSpan.FromSeconds(5));
            Assert.Equal(1, server.RequestCount);
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
