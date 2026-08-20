# SmartApp.Telemetry — Polish Plan

Baseline: builds clean (0 warnings), 12 tests pass, modular monolith, schema/indexes in place, no secrets in repo.
This plan fixes correctness bugs first, then performance, security, tests, and UX. Priorities: P0 (must), P1 (should), P2 (nice).

---

## Phase 0 — Hygiene (P0, ~1h) — DONE

- [x] Delete stale leftover folders from the old split-project layout (contain only bin/obj, untracked by git):
  `src/SmartApp.Telemetry.Api`, `src/SmartApp.Telemetry.Dashboard`, `src/Telemetry.Api`,
  `src/Telemetry.Dashboard`, `src/Telemetry.Infrastructure`, `clients/Telemetry.Client.DotNet`.
- [x] Remove dead code in `TelemetryIngestionService.IngestErrorsAsync`:
  `if (group.Id == Guid.Empty) group.Id = Guid.NewGuid();` — Id is always assigned at creation.
- [x] Enable analyzers in `Directory.Build.props` (`AnalysisLevel`, `TreatWarningsAsErrors` for Release) and fix findings.
- [x] Add `.editorconfig` matching AGENTS.md (4-space indent, naming rules).

## Phase 1 — Correctness bugs (P0) — DONE

1. [x] **Rate limiter is effectively global behind Cloudflare** — partitioned by `CF-Connecting-IP` with `OnRejected` logging.
2. [x] **Client retries permanent 4xx batches forever** — tri-state `SendResult` (Sent/Drop/RetryLater); 4xx dropped.
3. [x] **Login has no CSRF protection** — `RequireAntiforgeryToken` metadata + token check before reading the form.
4. [x] **ErrorGroup counters can be lost under concurrency** — atomic `ExecuteUpdateAsync` on relational providers with a tracked fallback.
5. [x] **Server-side property limits not enforced** — max 30 properties, key ≤ 100, string ≤ 2000, depth limit, array cap.
6. [x] **AdminKey comparison is not constant-time** — `CryptographicOperations.FixedTimeEquals`.
7. [x] **Client never sends heartbeats** — periodic heartbeat in the worker loop (`HeartbeatInterval`, default 15 min).
8. [x] **Maintenance starts at process start** — `Telemetry:MaintenanceInitialDelaySeconds` (default 30).

## Phase 2 — Performance (P1) — DONE

1. [x] **`GetApplicationAsync` loads unbounded data into memory** — SQL group-by aggregation; features windowed to 90 days.
2. [x] **`GetOverviewAsync` is N+1** — one grouped SQL query per metric.
3. [x] **Retention deletes in one transaction** — chunked `ExecuteDeleteAsync` (5000 rows per pass).
4. [x] Daily aggregates rewritten as SQL `GROUP BY` (see Phase 4).

## Phase 3 — Security hardening (P1) — DONE

- [x] `/openapi` no longer public in production (opt-in via `Api:ExposeOpenApi`).
- [x] Response security headers + CSP compatible with interactive server Blazor.
- [x] Nginx only forwards Cloudflare-injected `CF-*` headers (map-based).
- [x] Cookie `SecurePolicy = Always` outside Development (`Security:SecureCookies`).

## Phase 4 — Architecture cleanup (P1) — DONE

1. [x] **Remove the self-HTTP indirection** — `TelemetryApiClient` now calls domain services in-process; `TelemetryApi:BaseUrl` removed.
2. [x] **Slim down `Program.cs`** — endpoints extracted to `Endpoints/ApiEndpoints.cs`.
3. [x] Aggregation/retention SQL moved to `TelemetryAggregationService` (Infrastructure) with unit tests.

## Phase 5 — Test coverage — DONE (48 tests, up from 12)

- [x] Client: batching, 4xx drop, transient retry, installation ID stability, error route, offline queue bounds.
- [x] Server: property limits, heartbeat, resolve/regress, dashboard filters, DAU/WAU/MAU, aggregation idempotency, retention.
- [x] Web integration (`WebApplicationFactory` + InMemory): login, CSRF, admin key, rate limiting, application registration.

## Phase 6 — Dashboard UX polish (P2) — PARTIAL (deferred)

- [x] Empty states on every page, loading indicators (already present).
- [ ] Configurable dashboard timezone (UTC default) — deferred.
- [ ] Error details page: formatted context JSON, affected OS/country, copy button — deferred.
- [ ] Applications page: enable/disable, description edit — deferred.
- [ ] Mobile audit — deferred.

## Phase 7 — Docs, release, CI (P2) — DONE

- [x] README: architecture diagram (mermaid), env var reference table, API route list.
- [x] `CHANGELOG.md`, MIT `LICENSE`.
- [x] CI: GitHub Actions workflow + Azure Pipelines coverage and EF migration drift gate.
- [x] Docker: non-root user in the Web Dockerfile.

---

## Suggested execution order

1. Phase 0 + Phase 1 (correctness — small, high-value)
2. Phase 5 tests for everything fixed in (1)
3. Phase 2 (performance) + Phase 4 (cleanup)
4. Phase 3 (security)
5. Phase 6 + 7 (UX/docs/release)
