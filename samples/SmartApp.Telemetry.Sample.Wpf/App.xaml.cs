using System.Windows;
using SmartApp.Telemetry.Client;

namespace SmartApp.Telemetry.Sample.Wpf;

public partial class App : Application
{
    public ITelemetryClient Telemetry { get; }

    public App()
    {
        var endpoint = Environment.GetEnvironmentVariable("TELEMETRY_ENDPOINT") ?? "http://localhost:8091";
        var application = Environment.GetEnvironmentVariable("TELEMETRY_APP") ?? "sample-wpf";

        Telemetry = TelemetryFactory.Create(options =>
        {
            options.Endpoint = endpoint;
            options.Application = application;
            options.Version = "1.0.0";
            options.EnableAnalytics = true;
            options.EnableCrashReporting = true;
        });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        TelemetryExceptionHooks.AttachProcessWide(Telemetry);
        Telemetry.TrackAppStarted();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Telemetry.TrackAppClosed();
        await ((TelemetrySession)Telemetry).DisposeAsync();
        base.OnExit(e);
    }
}
