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

`samples/SmartApp.Telemetry.Sample.Wpf` — a WPF test harness that demonstrates:

- `TelemetryFactory` without a DI container,
- custom events with properties, feature/operation tracking,
- process-wide exception hooks (`AppDomain` + `TaskScheduler`), the WPF
  `DispatcherUnhandledException` hook, and caught exceptions,
- enabling/disabling, manual flushing, and burst load (200 / 1,000 events),
- flushing on exit.

It targets `net10.0-windows` and is **not** part of `Telemetry.sln`, so CI on Linux can skip it.
Open the project in Visual Studio or run:

~~~powershell
dotnet run --project samples/SmartApp.Telemetry.Sample.Wpf
~~~

## Configuration

Both samples send to `http://localhost:8091` by default. Change the `Endpoint` and `Application`
values in the sample source to point at your own server and registered app slug, or override
them at run time with the `TELEMETRY_ENDPOINT` and `TELEMETRY_APP` environment variables.
