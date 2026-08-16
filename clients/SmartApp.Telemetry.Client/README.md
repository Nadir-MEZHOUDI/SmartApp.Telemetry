# SmartApp.Telemetry.Client

Reusable, non-blocking telemetry SDK for .NET 10 applications such as WPF and WinForms.

## Install

~~~powershell
dotnet add package SmartApp.Telemetry.Client --version 4.8.16
~~~

The package is produced locally in D:\Programming\LocalNuget and published to the configured Azure Artifacts feed by the Azure DevOps pipeline.

## Use

~~~csharp
services.AddTelemetry(options =>
{
    options.Application = "my-app";
    options.Endpoint = "https://telemetry.example.com";
    options.Version = AppVersion.Current;
});
~~~

The client keeps a stable anonymous installation ID, batches events, retries briefly, and stores a bounded JSONL queue when the API is offline.
