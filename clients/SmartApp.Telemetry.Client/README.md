# SmartApp.Telemetry.Client

Reusable, non-blocking telemetry SDK for .NET applications such as WPF, WinForms and ASP.NET Core.

- Fire-and-forget: never throws, never blocks the UI thread.
- Keeps a stable anonymous installation ID locally (no hardware fingerprinting).
- Batches events, retries briefly, and stores a bounded JSONL queue when the API is offline.
- Optional global exception hooks for `AppDomain`, `TaskScheduler` and WPF `Dispatcher`.

## Install

~~~powershell
dotnet add package SmartApp.Telemetry.Client
~~~

## Use (with dependency injection)

~~~csharp
services.AddTelemetry(options =>
{
    options.Application = "my-app";
    options.Endpoint = "https://telemetry.example.com";
    options.Version = AppVersion.Current;
    options.EnableAnalytics = true;
    options.EnableCrashReporting = true;
});
~~~

Then resolve `ITelemetryClient`:

~~~csharp
telemetry.TrackAppStarted();
telemetry.TrackFeatureUsed("ExportPdf");

try
{
    // operation
}
catch (Exception exception)
{
    telemetry.TrackException(exception, new { operation = "ExportPdf" });
}
~~~

## Use without dependency injection

For simple WPF/WinForms apps that do not use a DI container:

~~~csharp
using var telemetry = TelemetryFactory.Create(options => options.Application = "my-app");

telemetry.TrackAppStarted();
~~~

`TelemetryFactory.Create` starts a background worker automatically. Dispose it (or call
`FlushAsync`) on app exit to send the remaining queued events.

## Process-wide exception hooks

~~~csharp
TelemetryExceptionHooks.AttachProcessWide(telemetry);
~~~

In WPF, also hook `Application.DispatcherUnhandledException` yourself (the SDK does not depend on WPF).
