# Repository Guidelines

## Project Structure & Module Organization

- src/SmartApp.Telemetry.Core: domain entities, contracts, validation, fingerprinting, and sanitization rules.
- src/SmartApp.Telemetry.Infrastructure: EF Core TelemetryDbContext, PostgreSQL mappings, ingestion/dashboard services, migrations, and maintenance jobs.
- src/SmartApp.Telemetry.Web: combined ASP.NET Core API and interactive Blazor Web App with Razor pages, shared components, API client, health checks, rate limiting, and Serilog configuration.
- clients/SmartApp.Telemetry.Client: standalone .NET 10 NuGet SDK for WPF, WinForms, and other applications.
- tests/SmartApp.Telemetry.Web.Tests and tests/SmartApp.Telemetry.Client.Tests: xUnit tests.
- nginx/, docker-compose*.yml, src/SmartApp.Telemetry.Web/Dockerfile, azure-pipelines.yml, and DeployToVPS.yml: deployment and packaging assets.

Keep shared behavior in the appropriate project; do not duplicate telemetry logic in consuming applications.

## Build, Test, and Development Commands

~~~powershell
dotnet restore Telemetry.sln
dotnet build Telemetry.sln --configuration Release
dotnet test Telemetry.sln --configuration Release --no-build --no-restore
dotnet run --project src/SmartApp.Telemetry.Web
docker compose up --build
~~~

The client project generates SmartApp.Telemetry.Client locally in D:\Programming\LocalNuget when built. Azure DevOps packs and pushes it to Azure Artifacts.

DeployToVPS.yml runs on main, publishes the Client package, pushes the SmartApp.Telemetry.Web image to GHCR, and deploys docker-compose.vps.yml over the vps-ssh service connection. Keep the VPS production .env and Compose file outside the repository.

## Coding Style & Naming Conventions

Use 4-space indentation, nullable reference types, implicit usings, file-scoped namespaces, and async methods with the Async suffix. Use PascalCase for types and public members, camelCase for private fields/parameters, and descriptive names for API routes. Keep timestamps UTC and validate payload limits at the ingestion boundary.

## Testing Guidelines

Use xUnit. Name tests by behavior, for example Errors_with_same_fingerprint_share_a_group. Add coverage for batching, offline persistence, sanitization, application isolation, validation, and dashboard calculations. Run the full solution test command before submitting changes.

## Security & Configuration

Never commit connection strings, admin keys, tokens, or customer data. Use environment variables such as ConnectionStrings__Telemetry and Dashboard__AdminKey. The ingestion API is intentionally public; preserve rate limiting, request-size limits, validation, Nginx limits, and Cloudflare country handling.

## Commits & Pull Requests

Use concise imperative commit subjects, for example Add client NuGet packaging or Convert dashboard to Blazor. Pull requests should explain the behavior change, list validation commands, mention migrations/configuration changes, and include dashboard screenshots when UI behavior changes.
