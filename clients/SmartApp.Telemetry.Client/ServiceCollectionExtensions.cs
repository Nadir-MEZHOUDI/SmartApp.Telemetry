using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartApp.Telemetry.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        Action<TelemetryOptions> configure)
    {
        var options = new TelemetryOptions
        {
            Endpoint = "http://localhost:5000",
            Application = "unknown"
        };
        configure(options);
        options.Validate();

        services.AddSingleton(Options.Create(options));
        services.AddHttpClient("SmartApp.Telemetry", client =>
        {
            client.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = options.HttpTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SmartApp.Telemetry.Client/1.0");
        });
        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<TelemetryClient>>();
            return new TelemetryClient(factory.CreateClient("SmartApp.Telemetry"), options, logger);
        });
        services.AddSingleton<ITelemetryClient>(sp => sp.GetRequiredService<TelemetryClient>());
        services.AddHostedService<TelemetryHostedService>();
        return services;
    }
}
