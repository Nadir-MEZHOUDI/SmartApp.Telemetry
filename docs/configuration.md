# Configuration Reference

The server is configured through environment variables (or appsettings.json). Never commit secrets.

## Server

| Setting | Default | Description |
| ------- | ------- | ----------- |
| `ConnectionStrings__Telemetry` | localhost | PostgreSQL connection string |
| `Dashboard__AdminKey` | *(empty)* | Header key (`X-Admin-Key`) for dashboard APIs; empty disables the header check |
| `Dashboard__Password` | *(empty)* | Dashboard login password; empty redirects to a "not configured" page |
| `TelemetryApi__BaseUrl` | `http://localhost:5000` | Used by the dashboard's internal API client |
| `UseInMemoryDatabase` | `false` | `true` uses an in-memory DB (tests/dev only) |
| `Telemetry:RawEventRetentionDays` | `90` | Retention of raw analytics events |
| `Telemetry:ErrorRetentionDays` | `180` | Retention of error occurrences |
| `Telemetry:MaintenanceIntervalHours` | `24` | Background maintenance interval |
| `Telemetry:MaintenanceInitialDelaySeconds` | `0` | Delay before the first maintenance run |
| `Telemetry:IngestionRateLimitPerMinute` | `120` | Ingestion requests per client per minute |
| `Telemetry:LoginRateLimitPerMinute` | `10` | Login attempts per minute |

## Client SDK (`TelemetryOptions`)

| Property | Default | Description |
| -------- | ------- | ----------- |
| `Endpoint` | `http://localhost:5000` | **Required** — telemetry server URL |
| `Application` | `unknown` | **Required** — registered application slug |
| `Version` | `unknown` | Application version string |
| `Enabled` | `true` | Master switch; `SetEnabled(false)` stops all sending |
| `EnableAnalytics` | `true` | Track events and heartbeats |
| `EnableCrashReporting` | `true` | Track exceptions |
| `StoragePath` | `%LocalAppData%\SmartAppTelemetry\<app>` | Where the offline queue is stored |
| `MaxBatchSize` | `50` | Max events per HTTP batch (1–50) |
| `FlushInterval` | `20s` | Background flush cadence |
| `HeartbeatInterval` | `15m` | Installation heartbeat cadence |
| `MaxQueueBytes` | `10 MB` | Offline queue size cap |
| `MaxInMemoryItems` | `1000` | In-memory queue cap (drops oldest) |
| `HttpTimeout` | `5s` | HTTP client timeout |

## Docker / `.env`

See [.env.example](../.env.example) for the Compose variables: `POSTGRES_DB`, `POSTGRES_USER`,
`POSTGRES_PASSWORD`, `ConnectionStrings__Telemetry`, `DASHBOARD_ADMIN_KEY`, `DASHBOARD_PASSWORD`,
`TELEMETRY_WEB_PORT`.
