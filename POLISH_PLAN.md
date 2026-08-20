# SmartApp.Telemetry — Polish Plan

Baseline: builds clean (0 warnings), 12 tests pass, modular monolith, schema/indexes in place, no secrets in repo.
This plan fixes correctness bugs first, then performance, security, tests, and UX. Priorities: P0 (must), P1 (should), P2 (nice).

---

## Phase 0 — Hygiene (P0, ~1h)

- [ ] Delete stale leftover folders from the old split-project layout (contain only bin/obj, untracked by git):
  `src/SmartApp.Telemetry.Api`, `src/SmartApp.Telemetry.Dashboard`, `src/Telemetry.Api`,
  `src/Telemetry.Dashboard`, `src/Telemetry.Infrastructure`, `clients/Telemetry.Client.DotNet`.
- [ ] Remove dead code in `TelemetryIngestionService.IngestErrorsAsync`:
  `if (group.Id == Guid.Empty) group.Id = Guid.NewGuid();` — Id is always assigned at creation.
- [ ] Enable analyzers in `Directory.Build.props` (`AnalysisLevel`, `TreatWarningsAsErrors` for Release) and fix findings.
- [ ] Add `.editorconfig` matching AGENTS.md (4-space indent, naming rules).

## Phase 1 — Correctness bugs (P0)

1. **Rate limiter is effectively global behind Cloudflare** — `Program.cs:67-82` uses the default partitioner (client IP), so every request appears to come from the Cloudflare edge and all apps share one 120/min bucket. Partition ingestion by `CF-Connecting-IP` (fallback to remote IP) and keep a separate edge-IP safety limit. Add `OnRejected` Serilog logging.
2. **Client retries permanent 4xx batches forever** — `TelemetryClient.SendBatchAsync` returns `false` on 4xx, so `FlushAsync` writes the batch to the offline JSONL queue and resends it on every startup. Change return to a tri-state (`Sent` / `Drop` / `RetryLater`): drop 4xx (except 429/408), retry 5xx.
3. **Login has no CSRF protection** — `/login/submit` minimal API (`Program.cs:315`) never validates an antiforgery token. Add `.RequireAntiforgery()` (or `IAntiforgery.ValidateRequestAsync`) and render the token in `Login.razor`.
4. **ErrorGroup counters can be lost under concurrency** — `IngestErrorsAsync` does read-modify-write on `TotalOccurrences`/`AffectedInstallations`; two concurrent batches race. Use atomic `ExecuteUpdateAsync` increments (Postgres) with a tracked fallback for InMemory tests. Also batch the per-occurrence `AnyAsync` affected-installation check instead of N+1.
5. **Server-side property limits not enforced** — `TelemetryRules.ValidateEvent` only checks that `Properties` is a JSON object. A 200KB string property passes today. Enforce the limits already specified in Telemetry_Plan.md §12: max 30 properties, key ≤ 100 chars, string ≤ 2000 chars, max depth — reject the batch otherwise. Apply the same to `AdditionalContext` on errors.
6. **AdminKey comparison is not constant-time** — `Program.cs:130` uses `string.Equals`. Use `CryptographicOperations.FixedTimeEquals`.
7. **Client never sends heartbeats** — `HeartbeatAsync` exists server-side but the SDK has no heartbeat path; `LastSeenAt` only updates on events. Either wire a periodic heartbeat in `TelemetryHostedService` or document that events alone drive activity.
8. **Maintenance starts at process start** — `TelemetryMaintenanceService.ExecuteAsync` runs immediately; add an initial delay so it doesn't compete with startup migrations, and log aggregate results.

## Phase 2 — Performance (P1)

1. **`GetApplicationAsync` loads unbounded data into memory** (`TelemetryDashboardService.cs:42-92`): all installations, all 30-day events, and *all* `feature_used` events ever (no date filter). Move group-by aggregation into SQL; window features to the last N days.
2. **`GetOverviewAsync` is N+1** (5 counts per app). Replace with one grouped SQL query per metric.
3. **Retention deletes in one transaction** (`TelemetryMaintenanceService.RunOnceAsync`): `ExecuteDelete` in chunks to avoid long table locks when rows are many.
4. Daily aggregates in `RunOnceAsync` load per-day events into memory; rewrite as SQL `GROUP BY` inserts.

## Phase 3 — Security hardening (P1)

- [ ] Stop treating `/openapi` as public in production (currently in `IsPublicRequest`) — or require the admin key.
- [ ] Add response security headers (X-Content-Type-Options, Referrer-Policy, frame options) and a CSP compatible with interactive server Blazor.
- [ ] Strip client-supplied `CF-IPCountry` in nginx (currently trusted from any request — spoofable when not behind Cloudflare); document the `real_ip` setup in `nginx/`.
- [ ] Set cookie `SecurePolicy = Always` when not in Development.

## Phase 4 — Architecture cleanup (P1)

1. **Remove the self-HTTP indirection** — `TelemetryApiClient` + `TelemetryApi:BaseUrl` are leftovers from the API/dashboard split. Dashboard components should call `TelemetryDashboardService`/ingestion services directly; delete the client and config.
2. **Slim down `Program.cs`** (386 lines) — extract endpoint mapping into `Endpoints`/`MapApiEndpoints` extension methods, and middleware into small helpers.
3. Move aggregation/retention SQL from `TelemetryMaintenanceService` (Web) into Infrastructure so it is unit-testable.

## Phase 5 — Test coverage (P0 for regressions, biggest effort)

Current: 12 tests (2 client, 10 web). Add, per Telemetry_Plan.md §49:

**Client SDK** (`tests/SmartApp.Telemetry.Client.Tests`)
- InstallationId persists across restarts and stays stable.
- Batch respects `MaxBatchSize`; queue byte limit evicts oldest.
- Retry on 429/5xx, drop on 4xx, no crash when endpoint unreachable.
- `SetEnabled(false)` stops all sends.
- `TelemetryExceptionHooks` deduplicates the same exception.

**Server** (`tests/SmartApp.Telemetry.Web.Tests`)
- Validation: oversized property, too many properties, unknown event name, unknown/disabled app, empty/oversized batch, bad heartbeat.
- Error lifecycle: fingerprint stability across builds, resolve → regress flow.
- Dashboard queries: DAU/WAU/MAU semantics with seeded data; errors filters (status/search/version/pagination); installation filters.
- Maintenance: aggregates are idempotent across re-runs; retention deletes only old rows.
- Web integration (`WebApplicationFactory` + InMemory): login success/failure, non-HTML 401 vs HTML redirect, rate limit 429, dashboard auth middleware.

Target: cover every public method in `TelemetryIngestionService`, `TelemetryDashboardService`, `TelemetryRules`, `DashboardAuthentication`, and the client SDK.

## Phase 6 — Dashboard UX polish (P2)

- [ ] Empty states on every page (no data yet), loading indicators for interactive components.
- [ ] Configurable dashboard timezone (UTC default) — "today" currently means UTC day regardless of viewer.
- [ ] Error details page: formatted context JSON, affected OS/country, copy button for stack trace.
- [ ] Applications page: enable/disable, description edit.
- [ ] Mobile audit: tables scroll, cards stack.

## Phase 7 — Docs, release, CI (P2)

- [ ] README: architecture diagram (mermaid), env var reference table, API route list.
- [ ] Client package: set a real version (e.g. 1.0.0), `CHANGELOG.md`, MIT `LICENSE`.
- [ ] CI (both `azure-pipelines.yml` and `.github/workflows`): add coverage summary (coverlet), `dotnet ef migrations has-pending-model-changes` gate, and a compose smoke test hitting `/health`.
- [ ] Docker: verify non-root user in the Web Dockerfile.

---

## Suggested execution order

1. Phase 0 + Phase 1 (correctness — small, high-value)
2. Phase 5 tests for everything fixed in (1)
3. Phase 2 (performance) + Phase 4 (cleanup)
4. Phase 3 (security)
5. Phase 6 + 7 (UX/docs/release)
