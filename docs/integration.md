# Integrating the Client SDK

`SmartApp.Telemetry.Client` is a reusable, non-blocking telemetry SDK for WPF, WinForms, ASP.NET Core, and
any other .NET 10 application.

## Install

~~~powershell
dotnet add package SmartApp.Telemetry.Client
~~~

## Configure

### With dependency injection

~~~csharp
using SmartApp.Telemetry.Client;

services.AddTelemetry(options =>
{
    options.Application = "smartpharm";          // required — registered slug
    options.Endpoint = "https://telemetry.example.com"; // required — your server URL
    options.Version = "1.0.0";
    options.EnableAnalytics = true;              // default true
    options.EnableCrashReporting = true;         // default true
});
~~~

Then resolve `ITelemetryClient` anywhere DI is available.

### Without dependency injection

For simple WPF/WinForms apps that don't use a DI container:

~~~csharp
using var telemetry = TelemetryFactory.Create(options =>
{
    options.Application = "smartpharm";
    options.Endpoint = "https://telemetry.example.com";
});

telemetry.TrackAppStarted();
~~~

`TelemetryFactory.Create` starts a background worker automatically. Dispose it on app exit (or call
`FlushAsync`) so remaining queued events are sent.

## Track events

~~~csharp
telemetry.TrackAppStarted();
telemetry.TrackAppClosed();
telemetry.TrackFeatureUsed("ExportPdf");
telemetry.TrackOperationSucceeded("Backup");
telemetry.TrackOperationFailed("ImportProducts", exception);
telemetry.Track("custom_event", new { step = 3, source = "settings" });
~~~

## Report exceptions

~~~csharp
try { await SaveAsync(); }
catch (Exception exception)
{
    telemetry.TrackException(exception);
    // or with context:
    telemetry.TrackException(exception, new { operation = "Save", id = orderId });
}
~~~

Exceptions are grouped by fingerprint on the server, so repeated occurrences of the same bug collapse into a
single error group.

## Process-wide exception hooks

~~~csharp
TelemetryExceptionHooks.AttachProcessWide(telemetry);
~~~

This hooks `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`.
In WPF, also hook `Application.DispatcherUnhandledException` yourself (the SDK does not depend on WPF):

~~~csharp
Application.Current.DispatcherUnhandledException += (_, args) =>
{
    telemetry.TrackException(args.Exception);
};
~~~

## Flush and shutdown

~~~csharp
await telemetry.FlushAsync();
~~~

When the app is shutting down, dispose the session or stop the host so the worker flushes the queue.

## Offline behavior

If the server is unreachable, the SDK never throws. It stores a bounded JSONL queue on disk:

~~~text
%LocalAppData%/SmartAppTelemetry/<app-name>/telemetry-queue.jsonl
~~~

The queue is capped (default 10 MB) and retried on the next start. Events are dropped oldest-first beyond the cap.

## What not to do

- Don't put secrets or tokens in the SDK configuration — the ingestion API is public by design and the client
  is meant to be shipped with your (possibly open-source) app.
- Don't track raw logs. Keep detailed logging with Serilog; send structured events and exceptions to telemetry.
- Don't send customer data, files, or database contents. Sanitization runs on the client and server.
