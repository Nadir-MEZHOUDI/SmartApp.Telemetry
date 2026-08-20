using System.Windows;
using SmartApp.Telemetry.Client;

namespace SmartApp.Telemetry.Sample.Wpf;

public partial class App : Application
{
    public ITelemetryClient Telemetry { get; }

    public App()
    {
        Telemetry = TelemetryFactory.Create(options =>
        {
            options.Application = "sample-wpf";
            options.Endpoint = "http://localhost:8091";
            options.Version = "1.0.0";
            options.EnableAnalytics = true;
            options.EnableCrashReporting = true;
        });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        TelemetryExceptionHooks.AttachProcessWide(Telemetry);
        DispatcherUnhandledException += (_, args) =>
        {
            Telemetry.TrackException(args.Exception);
        };

        Telemetry.TrackAppStarted();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Telemetry.TrackAppClosed();
        await ((TelemetrySession)Telemetry).DisposeAsync();
        base.OnExit(e);
    }
}
