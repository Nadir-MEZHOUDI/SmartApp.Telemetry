# SmartApp.Telemetry

[![CI](https://github.com/Nadir-MEZHOUDI/SmartApp.Telemetry/actions/workflows/ci.yml/badge.svg)](https://github.com/Nadir-MEZHOUDI/SmartApp.Telemetry/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/SmartApp.Telemetry.Client)](https://www.nuget.org/packages/SmartApp.Telemetry.Client)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

A self-hosted, centralized telemetry platform for your .NET applications — WPF, WinForms, and ASP.NET Core.
No PostHog, no Sentry, no third-party SaaS. You own your data.

It gives you usage analytics, crash reporting, version/OS/country breakdowns, and a Blazor dashboard,
all powered by a single PostgreSQL database.

- **Non-blocking SDK** — fire-and-forget, never throws, never slows your UI.
- **Anonymous** — stable installation ID, no emails, no hardware fingerprinting, no IPs stored.
- **Multi-app** — one server, dozens of applications.
- **Simple** — add a NuGet package, configure, done.

> 🇩🇿/🇸🇦 العربية: وصف المشروع بالعربية في [القسم السفلي](#العربية).

---

## Features

- Usage analytics: DAU / WAU / MAU, feature usage, app versions, OS, architecture, language, country.
- Crash reporting: exception grouping via fingerprinting, stack traces, affected installations, resolved/regressed errors.
- Interactive Blazor dashboard with charts, protected by a password and admin key.
- Offline persistence: the client keeps a bounded JSONL queue and retries when the API is unreachable.
- Built for scale: batching (up to 50 events), daily aggregates, retention policies, background maintenance.
- Hardened ingestion: rate limiting, request-size limits, validation, allowed event names, Cloudflare country header.
- Docker + Nginx + Cloudflare friendly; deploys to a single VPS.

## Architecture

```text
      WPF / WinForms / ASP.NET apps
                  │
        SmartApp.Telemetry.Client (NuGet)
                  │
            HTTPS / JSON / Batch
                  │
             Cloudflare ──► Nginx
                  │
        SmartApp.Telemetry.Web (API + Dashboard)
                  │
             PostgreSQL (single DB)
```

A modular monolith — no microservices, no Kafka, no Redis, no ClickHouse. PostgreSQL is enough for phase one.

## Quick start (self-hosted)

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Docker (or PostgreSQL).

### Option A — Docker Compose

~~~powershell
Copy-Item .env.example .env   # then edit the passwords
docker compose up --build
~~~

Open the dashboard at `http://localhost:8091`, the API health at `http://localhost:8091/health`,
and OpenAPI at `http://localhost:8091/openapi/v1.json`.

### Option B — Run locally with PostgreSQL

~~~powershell
dotnet restore Telemetry.sln
dotnet run --project src/SmartApp.Telemetry.Web
~~~

Before opening the dashboard set the password via `Dashboard__Password` (in Docker use `DASHBOARD_PASSWORD`
in `.env`).

> Change the PostgreSQL, `Dashboard__AdminKey` and `Dashboard__Password` values before any public deployment.

## Integrate an application

Add the SDK to any .NET app:

~~~powershell
dotnet add package SmartApp.Telemetry.Client
~~~

With dependency injection:

~~~csharp
services.AddTelemetry(options =>
{
    options.Application = "smartpharm";
    options.Endpoint = "https://telemetry.example.com";
    options.Version = AppVersion.Current;
    options.EnableAnalytics = true;
    options.EnableCrashReporting = true;
});
```

Without dependency injection (simple WPF/WinForms):

~~~csharp
using var telemetry = TelemetryFactory.Create(options =>
{
    options.Application = "smartpharm";
    options.Endpoint = "https://telemetry.example.com";
});

telemetry.TrackAppStarted();
telemetry.TrackFeatureUsed("ExportPdf");

try { /* operation */ }
catch (Exception exception)
{
    telemetry.TrackException(exception, new { operation = "ExportPdf" });
}
```

Full integration guide: [docs/integration.md](docs/integration.md).

## Supported events

```
app_first_started, app_started, app_closed,
feature_used,
operation_completed, operation_failed,
update_available, update_started, update_completed, update_failed,
exception, fatal_exception
```

## Register a new application

~~~powershell
curl -X POST http://localhost:8091/api/v1/applications `
  -H "Content-Type: application/json" `
  -d '{"name":"SmartPharm","slug":"smartpharm"}'
~~~

## Repository layout

```
src/SmartApp.Telemetry.Core            domain, validation, fingerprinting, sanitization
src/SmartApp.Telemetry.Infrastructure  EF Core, PostgreSQL, ingestion/dashboard services, maintenance
src/SmartApp.Telemetry.Web             ASP.NET Core API + Blazor dashboard (single deployable)
clients/SmartApp.Telemetry.Client      reusable NuGet SDK (WPF/WinForms/ASP.NET Core)
samples/SmartApp.Telemetry.Sample.Console  console sample
samples/SmartApp.Telemetry.Sample.Wpf     WPF sample with one-click Register App
tests/SmartApp.Telemetry.Web.Tests     API / ingestion / dashboard / aggregation tests
tests/SmartApp.Telemetry.Client.Tests  client batching / queue / sanitization tests
docs/                                  getting-started, integration, configuration, deployment
```

> `CHIFA.Server` has been removed — the platform is now a modular monolith with a single
> deployable `SmartApp.Telemetry.Web`. See `Telemetry.sln` for the current project list.

## Documentation

- [Getting started](docs/getting-started.md)
- [Integrating the client SDK](docs/integration.md)
- [Configuration reference](docs/configuration.md)
- [Self-hosting & deployment](docs/deployment.md)
- [API reference](docs/api.md)

## Security

- The ingestion API is intentionally public; keep rate limiting, Nginx limits and Cloudflare enabled.
- The client SDK contains **no secrets** by design.
- The dashboard is protected by `Dashboard__Password` (cookie session) and `Dashboard__AdminKey` (API header).
- See [SECURITY.md](SECURITY.md).

## Contributing

Contributions are welcome! Read [CONTRIBUTING.md](CONTRIBUTING.md), then open an issue or pull request.
Please run the full test suite before submitting:

~~~powershell
dotnet restore Telemetry.sln
dotnet build Telemetry.sln --configuration Release
dotnet test Telemetry.sln --configuration Release --no-build --no-restore
```

## License

[MIT](LICENSE)

---

<a name="العربية"></a>

## العربية

منصة **Telemetry مركزية خاصة** بتطبيقات .NET لديك (WPF/WinForms/ASP.NET)، دون الاعتماد على PostHog أو Sentry:

- Modular monolith: `Web` (API + Blazor Dashboard)، `Core`، `Infrastructure` (EF Core + PostgreSQL)
- SDK مشتركة `SmartApp.Telemetry.Client` تُنشر NuGet — queue + batching حتى 50 حدثًا + offline JSONL queue، fire-and-forget
- Multi-app عبر `Application`/`ApplicationId` مع Installation ID مجهول محلي
- تتبع DAU/WAU/MAU، الإصدارات، الدول، نظام التشغيل، الميزات، وتجميع الأخطاء (fingerprinting) مع resolve/regress
- حماية: rate limiting، sanitization، حدود payload، بدون أسرار في SDK
- نشر: Docker Compose + Nginx + Cloudflare + VPS

### التشغيل المحلي

```powershell
dotnet restore Telemetry.sln
dotnet run --project src/SmartApp.Telemetry.Web
```

أو شغّل كل شيء عبر `docker compose up --build`. اضبط كلمة مرور Dashboard عبر `Dashboard__Password` قبل فتحه.
