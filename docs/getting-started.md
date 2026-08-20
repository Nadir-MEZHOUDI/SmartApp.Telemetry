# Getting Started

This guide walks you through running the platform for the first time and connecting your first application.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (recommended) or a local PostgreSQL instance

## 1. Run the server

### Docker Compose (easiest)

~~~powershell
Copy-Item .env.example .env
# edit .env and set strong passwords
docker compose up --build
~~~

Open:

| What | URL |
| ---- | --- |
| Dashboard | http://localhost:8091 |
| Health | http://localhost:8091/health |
| OpenAPI | http://localhost:8091/openapi/v1.json |

### Local development

~~~powershell
dotnet restore Telemetry.sln
dotnet run --project src/SmartApp.Telemetry.Web
~~~

The API reads the connection string from `ConnectionStrings__Telemetry` (see
[configuration](configuration.md)). On first start, migrations run automatically.

## 2. Register your application

~~~powershell
curl -X POST http://localhost:8091/api/v1/applications `
  -H "Content-Type: application/json" `
  -d '{"name":"SmartPharm","slug":"smartpharm"}'
~~~

You can add dozens of applications; each one has its own slug and dashboard view.

## 3. Connect a .NET application

Install the SDK and configure it. Full details in [integration.md](integration.md):

~~~powershell
dotnet add package SmartApp.Telemetry.Client
~~~

~~~csharp
services.AddTelemetry(options =>
{
    options.Application = "smartpharm";
    options.Endpoint = "http://localhost:8091";
    options.Version = "1.0.0";
});
~~~

Run the app, perform a few actions, then open the dashboard — you should see the new installation,
active devices, events, and any exceptions.

## 4. Definition of done

When this works, you're up and running:

- Two different machines show `Installations: 2` and `Active Today: 2` in the dashboard.
- An exception in your app appears as an error group with stack trace and affected installations.
- `telemetry.TrackFeatureUsed("ExportPdf")` shows up under most used features.
- If you stop the telemetry server, your app keeps working normally with no visible error or slowdown.
