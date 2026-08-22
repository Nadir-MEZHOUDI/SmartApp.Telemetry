using System.Diagnostics;
using System.Windows;
using SmartApp.Telemetry.Client;

namespace SmartApp.Telemetry.Sample.Wpf;

public partial class App : Application
{
    public ITelemetryClient Telemetry { get; }
    public string Endpoint { get; }
    public string ApplicationName { get; }
    public Guid InstallationId => Telemetry.InstallationId;

    public App()
    {
        Endpoint = Debugger.IsAttached? "http://localhost:5000": "https://telemetry.smartappdz.org/";
        ApplicationName = Environment.GetEnvironmentVariable("TELEMETRY_APP") ?? "sample-wpf";

        Telemetry = TelemetryFactory.Create(options =>
        {
            options.Endpoint = Endpoint;
            options.Application = ApplicationName;
            options.Version = "1.0.0";
            options.EnableAnalytics = true;
            options.EnableCrashReporting = true;
        }, new WpfBridgeLogger());
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
