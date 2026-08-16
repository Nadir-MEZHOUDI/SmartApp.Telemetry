namespace SmartApp.Telemetry.Client;

public static class TelemetryExceptionHooks
{
    public static void AttachProcessWide(ITelemetryClient telemetry)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                telemetry.TrackException(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            telemetry.TrackException(args.Exception);
            args.SetObserved();
        };
    }
}
