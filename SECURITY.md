# Security Policy

SmartApp.Telemetry is designed to be self-hosted. The ingestion API is intentionally public, so it relies on
defense-in-depth: rate limiting, request-size limits, validation, Nginx limits, and Cloudflare.

## Supported versions

| Version | Supported          |
| ------- | ------------------ |
| latest  | :white_check_mark: |

We recommend always running the latest release.

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Instead, send a private report through one of:

- GitHub Security Advisories: use the "Report a vulnerability" button on the repository's Security tab.
- Direct email to the maintainers listed in the repository metadata.

Please include:

- A description of the vulnerability and its impact.
- Steps to reproduce.
- Affected components and versions.
- Any suggested fix, if you have one.

We will acknowledge reports within 72 hours and work on a fix. Once a fix is released, we will credit
reporters unless they prefer to stay anonymous.

## Security guidelines for self-hosting

- Change `Dashboard__Password` and `Dashboard__AdminKey` before any public deployment.
- Use strong, randomly generated passwords (see `.env.example`).
- Keep Nginx and Cloudflare in front of the API; do not expose PostgreSQL publicly.
- The `SmartApp.Telemetry.Client` SDK contains no secrets by design; do not add any.
