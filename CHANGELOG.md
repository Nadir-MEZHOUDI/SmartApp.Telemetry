# Changelog

All notable changes to SmartApp.Telemetry are documented here.

## Unreleased

### Security

- Rate limiting now partitions by `CF-Connecting-IP` so all applications share one bucket only when they truly share one client.
- Login is protected against CSRF with antiforgery tokens.
- Admin key comparison is constant-time.
- Security headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, CSP) on all responses.
- OpenAPI is no longer public in production (opt-in via `Api__ExposeOpenApi`).
- Secure auth cookies are forced outside Development (`Security__SecureCookies`).
- Nginx only forwards Cloudflare-injected `CF-*` headers, preventing spoofed country codes.

### Fixed

- Client SDK no longer requeues permanently rejected (4xx) batches into the offline JSONL queue.
- Error group counters are updated atomically, so concurrent batches cannot lose occurrences.
- Server now enforces property limits (30 keys, key ≤ 100 chars, string ≤ 2000 chars, depth limit) at the ingestion boundary.
- Client SDK sends periodic heartbeats so `LastSeenAt` stays fresh even without events.

### Performance

- Dashboard overview and application views aggregate in SQL instead of loading rows into memory.
- Feature usage is windowed to the last 90 days.
- Retention cleanup deletes in bounded chunks to avoid long table locks.
- Daily aggregates are rebuilt with SQL `GROUP BY`.

### Changed

- Dashboard components call domain services directly instead of round-tripping through their own HTTP API.
- API endpoints extracted from `Program.cs` into `Endpoints/ApiEndpoints.cs`.
- Maintenance SQL moved into `TelemetryAggregationService` (Infrastructure) with unit tests.
- Code analyzers enabled with the recommended ruleset; warnings fixed or documented.
- `TelemetryClient` and its queue are disposable; the hosted service flushes and disposes on shutdown.

### Tests

- Client: batching, 4xx drop, transient retry, installation ID stability, error route.
- Server: property limits, heartbeat, resolve/regress lifecycle, dashboard filters, DAU/WAU/MAU, aggregation idempotency, retention.
- Web integration: login, CSRF, admin key, rate limiting, application registration.

### CI

- GitHub Actions CI workflow (build, test, coverage, EF migration drift check).
- Azure Pipelines now collect and publish code coverage.
