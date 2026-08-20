# Self-Hosting & Deployment

SmartApp.Telemetry is designed to run on a single VPS behind Nginx and Cloudflare.

## Docker Compose (local)

The repository's `docker-compose.yml` starts the web service and PostgreSQL together on an internal network.
It is self-contained — no external network or pre-created volume is required:

~~~powershell
Copy-Item .env.example .env   # set strong passwords first
docker compose up --build
~~~

The web service is published on `127.0.0.1:${TELEMETRY_WEB_PORT:-8091}:8080` and forwards
`X-Forwarded-*` headers, so it can sit behind Nginx.

## Production layout (VPS)

```text
Cloudflare
   │
Nginx (TLS, request limits, proxy headers)
   │
SmartApp.Telemetry.Web :8080
   │
PostgreSQL (same host, private network)
```

Recommended steps:

1. Create the `.env` and Compose file on the VPS (kept **outside** the repository).
2. Use `CF-IPCountry` handling and Nginx request limits to protect the public ingestion API.
3. Point Nginx at `127.0.0.1:8091` (or the Compose-published port) with TLS via certbot/Cloudflare.

## CI/CD

- **CI** — `.github/workflows/ci.yml` builds and tests the solution on every push/PR (GitHub Actions).
- **NuGet** — `.github/workflows/publish-nuget.yml` packs and pushes `SmartApp.Telemetry.Client`
  to nuget.org when a `client-v*` tag is pushed. Set the `NUGET_API_KEY` secret.
- **VPS** — `DeployToVPS.yml` (Azure DevOps) builds the image, pushes it to GHCR, and deploys over SSH.
  It references the owner's private service connections (`ghcr-login`, `vps-ssh`) and a
  `docker-compose.vps.yml` kept outside the repository; adapt it to your own infrastructure.

## Retention

Raw events are kept 90 days and error occurrences 180 days by default; installations, error groups, and
daily aggregates are kept forever. Configure via `Telemetry:RawEventRetentionDays` and
`Telemetry:ErrorRetentionDays`. A background maintenance service performs the cleanup.

## Database

Migrations run automatically on API start. The initial migration lives in
`src/SmartApp.Telemetry.Infrastructure/Migrations`. All timestamps are UTC.
