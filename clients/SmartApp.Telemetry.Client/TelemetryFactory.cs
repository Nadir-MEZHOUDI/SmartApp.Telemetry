using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartApp.Telemetry.Client;

public static class TelemetryFactory
{
    public static TelemetrySession Create(Action<TelemetryOptions> configure) =>
        Create(configure, NullLogger<TelemetryClient>.Instance);

    public static TelemetrySession Create(Action<TelemetryOptions> configure, ILogger<TelemetryClient> logger)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(logger);

        var options = new TelemetryOptions
        {
            Endpoint = "http://localhost:5000",
            Application = "unknown"
        };
        configure(options);
        options.Validate();

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = options.HttpTimeout
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SmartApp.Telemetry.Client/1.0");

        var client = new TelemetryClient(httpClient, options, logger);
        return new TelemetrySession(client);
    }
}
