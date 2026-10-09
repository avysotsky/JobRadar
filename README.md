# JobRadar

.NET 10 prototype for personal job-vacancy ingestion. Repository: https://github.com/avysotsky/JobRadar

**Status:** Phase 1 prototype, not a production-ready exhaustive crawler. See [current coverage limitations](docs/STATUS.md).

## Included
- DOU RSS adapter and Djinni draft HTML adapter
- HTTP fetcher with timeouts/retries and throttling
- PostgreSQL tables for vacancy details, crawl run reports and fetch errors
- JSON crawl reports
- xUnit parser fixture tests
- GitHub Actions build/test workflow

## Requirements
- .NET 10 SDK
- PostgreSQL database (e.g. localhost:5432)

## Set up PostgreSQL
Create a database and dedicated user, for example from `scripts/init-db.sql`. **Change the placeholder password** before use; do not commit credentials.

For the current PowerShell session:

```powershell
$env:JOBRADAR_DB = "Host=localhost;Port=5432;Database=jobradar;Username=jobradar;Password=<your password>"
```

## Build and test
```powershell
dotnet restore tests/JobRadar.Tests/JobRadar.Tests.csproj
dotnet build tests/JobRadar.Tests/JobRadar.Tests.csproj -c Release
dotnet test tests/JobRadar.Tests/JobRadar.Tests.csproj -c Release
```

## Run a single scan
```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --once
```

Scheduled mode (long-running process):
```powershell
dotnet run --project src/JobRadar/JobRadar.csproj
```
Configured Kyiv hours: 08:00, 13:00, 19:00. Output reports are written under `reports/`.

## Coverage and safety
DOU now reads its public RSS feed. The feed's completeness is unverified, so the report marks it partial. Djinni live parsing and paging are unverified. A report status of `UNVERIFIED_COVERAGE` does not mean exhaustive success. Full vacancy description requires successful detail fetch; an unavailable source is not considered proof of no jobs. The project must comply with website usage policies and rate limits. No CAPTCHA/auth bypassing.

This code has not yet been validated against live DOU/Djinni markup; build status must be confirmed using CI.
