# Samples

Ready-to-run examples showing how to integrate `SmartApp.Telemetry.Client` into your own applications.

## Console sample (any platform)

`samples/SmartApp.Telemetry.Sample.Console` — the simplest possible integration using
`TelemetryFactory` (no DI container). It is part of `Telemetry.sln`, so it builds in CI.

Run it (with the telemetry server running):

~~~powershell
dotnet run --project samples/SmartApp.Telemetry.Sample.Console
~~~

## WPF sample (Windows only)

`samples/SmartApp.Telemetry.Sample.Wpf` — a small WPF app that demonstrates:

- `TelemetryFactory` without a DI container,
- feature tracking from button clicks,
- process-wide exception hooks (`AppDomain` + `TaskScheduler`) and the WPF
  `DispatcherUnhandledException` hook,
- flushing on exit.

It targets `net10.0-windows` and is **not** part of `Telemetry.sln`, so CI on Linux can skip it.
Open the project in Visual Studio or run:

~~~powershell
dotnet run --project samples/SmartApp.Telemetry.Sample.Wpf
~~~

## Configuration

Both samples send to `http://localhost:8091` by default. Change the `Endpoint` and `Application`
values in the sample source to point at your own server and registered app slug.
