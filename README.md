# JobRadar

.NET 10 vacancy-ingestion prototype: [GitHub repository](https://github.com/avysotsky/JobRadar).

**Status:** Working Robota.ua search + complete vacancy text integration verified against live public APIs. Not an exhaustive Ukrainian-job-market index. See [coverage limitations](docs/STATUS.md).

## Sources

| Source | Discovery | Full details | Real-world validation |
|---|---|---|---|
| Robota.ua | Public JSON search `api.rabota.ua/vacancy/search` | Public published-company vacancies JSON `api.robota.ua/companies/{id}/published-vacancies` | Search and one full vacancy verified in GitHub Actions |
| DOU | Public filtered RSS | Public job HTML | Synthetic tests only; feed may be capped |
| Djinni | Draft public search pagination | Draft HTML parser | Synthetic tests only |
| Work.ua | Not yet implemented | — | — |
| Jooble | Not yet implemented | — | — |

Robota company-detail responses are cached **per scan, per company**. Records are persisted with source URL, title, company, date and preview **before** a full-detail attempt. Missing details remain visible as `fetch_errors`. Some companies return only a portion of their vacancies; this is reported as partial coverage, not treated as an empty vacancy.

## Requirements

.NET 10 SDK and PostgreSQL 16+. The application runs on Windows and Linux. Create a dedicated PostgreSQL database/user with privileges to create tables (see `scripts/init-db.sql`). Change the placeholder credentials. Never commit passwords or other secrets.

```powershell
git clone https://github.com/avysotsky/JobRadar.git
cd JobRadar
$env:JOBRADAR_DB = "Host=localhost;Port=5432;Database=jobradar;Username=jobradar;Password=<your password>"
dotnet restore tests/JobRadar.Tests/JobRadar.Tests.csproj
dotnet test tests/JobRadar.Tests/JobRadar.Tests.csproj -c Release
dotnet run --project src/JobRadar/JobRadar.csproj -- --once
```

Run as a long-running scheduler:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj
```

Configured Kyiv hours: 08:00, 13:00, 19:00. JSON run reports are written under `reports/`. Database records are stored in `jobs`, `crawl_runs` and `fetch_errors`. Configure search queries in `src/JobRadar/appsettings.json`. `RobotaQueries` currently includes `.net`, `c-sharp`, `backend` — these do not cover all specialties.

Live API smoke without PostgreSQL:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-robota
```

[.NET CI](https://github.com/avysotsky/JobRadar/actions/workflows/dotnet.yml) runs xUnit and disposable PostgreSQL integration tests. [Robota.ua live smoke](https://github.com/avysotsky/JobRadar/actions/workflows/robota-smoke.yml) checks search JSON plus the full description of a known public vacancy.

## Coverage and compliance

A green smoke test proves the particular requests worked from a GitHub runner, not every Ukrainian vacancy was discovered. The project does not circumvent authentication, CAPTCHA, or platform controls. Respect rate limits and source policies; outages, parsing failures and unknown coverage must never be presented as 'no jobs'.
