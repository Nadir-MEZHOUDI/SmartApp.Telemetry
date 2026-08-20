using SmartApp.Telemetry.Client;

// The simplest possible integration: no DI container, no hosting.
// TelemetryFactory.Create starts a background worker automatically.
await using var telemetry = TelemetryFactory.Create(options =>
{
    options.Application = "sample-console";
    options.Endpoint = "http://localhost:8091"; // your telemetry server
    options.Version = "1.0.0";
    options.EnableAnalytics = true;
    options.EnableCrashReporting = true;
});

telemetry.TrackAppStarted();
telemetry.TrackFeatureUsed("Run");

try
{
    await Task.Delay(1000);
    telemetry.TrackOperationSucceeded("Demo");
}
catch (Exception exception)
{
    telemetry.TrackException(exception, new { operation = "Demo" });
}

await telemetry.FlushAsync();

Console.WriteLine();
Console.WriteLine("Events sent. Open the dashboard to see this installation and its events.");
Console.WriteLine("Telemetry server must be running. See the repository README for quick start.");
