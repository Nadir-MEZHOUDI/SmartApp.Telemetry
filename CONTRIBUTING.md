# Contributing

Thanks for considering contributing to SmartApp.Telemetry. We welcome bug reports, feature requests,
documentation, and code contributions.

## Getting started

1. Fork the repository and clone your fork.
2. Run the solution once to make sure everything works locally:
   ~~~powershell
   dotnet restore Telemetry.sln
   dotnet build Telemetry.sln --configuration Release
   dotnet test Telemetry.sln --configuration Release --no-build --no-restore
   ~~~
3. Create a feature branch: `git checkout -b feature/my-change`.

## Project layout

- `src/SmartApp.Telemetry.Core` – domain entities, contracts, validation, fingerprinting, sanitization.
- `src/SmartApp.Telemetry.Infrastructure` – EF Core `TelemetryDbContext`, PostgreSQL mappings, services, migrations, maintenance jobs.
- `src/SmartApp.Telemetry.Web` – ASP.NET Core API + Blazor dashboard.
- `clients/SmartApp.Telemetry.Client` – reusable NuGet SDK for consuming apps.
- `tests/` – xUnit tests for Web and Client.

## Coding style

- 4-space indentation, nullable reference types, implicit usings, file-scoped namespaces.
- Async methods end with the `Async` suffix. Timestamps are UTC.
- Do not add telemetry logic to consuming applications; keep shared behavior in the SDK.
- `TreatWarningsAsErrors` is enabled in `Directory.Build.props`; keep the build warning-free.

## Tests

- Name tests by behavior, e.g. `Errors_with_same_fingerprint_share_a_group`.
- Add coverage for batching, offline persistence, sanitization, application isolation, validation, and dashboard calculations.
- Run the full solution test command above before submitting.

## Security

- Never commit connection strings, admin keys, tokens, or customer data.
- Use environment variables such as `ConnectionStrings__Telemetry` and `Dashboard__AdminKey`.
- The ingestion API is intentionally public; preserve rate limiting, request-size limits, validation, and Nginx limits.
- See [SECURITY.md](SECURITY.md) for reporting vulnerabilities.

## Pull requests

- Explain the behavior change and list the validation commands you ran.
- Mention any migrations or configuration changes.
- Include dashboard screenshots when UI behavior changes.
- Keep the diff focused; if a change is large, open an issue first to discuss it.
