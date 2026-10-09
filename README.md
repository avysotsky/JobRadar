# JobRadar

.NET 10 vacancy ingestion for Ukrainian software development jobs — [GitHub repository](https://github.com/avysotsky/JobRadar).

**Status:** active prototype with verified Robota.ua and DOU partial-feed ingestion. This is **not** an exhaustive crawler; read the [coverage status](docs/STATUS.md) and [coverage design](docs/PHASE2_COVERAGE.md).

## Sources and evidence

| Site | Discovery | Full-text | Evidence |
| --- | --- | --- | --- |
| Robota.ua | Public JSON search API | Public company published-vacancies JSON | Real .NET search, total reconciliation, and Credit Agricole full vacancy retrieved in GitHub Actions |
| DOU | RSS | Public vacancy HTML | Real RSS returned 25 entries; sample detail length 4,625 characters |
| Djinni | Public RSS | Public vacancy HTML | Live smoke: 54 RSS entries, sample full text 1,891 characters; coverage unverified |
| Work.ua | Draft HTML search, **disabled by default** | Draft HTML | GitHub runner returned HTTP 403; do not bypass access controls |
| Jooble | Official regional REST API, opt-in only | Search snippets only | Client, key protection and PostgreSQL quota guard tested; no live key used |

## Design

- Store discovered vacancies **before** attempting their full descriptions.
- Canonical IDs avoid duplicate Robota listings across several queries (for example `.net` and `backend`).
- `job_queries` preserves the keyword query provenance of every discovered vacancy.
- `fetch_errors` tracks unreadable descriptions and source/network errors.
- `QUERY_RECONCILED` indicates matching the API's total for **one query**, not complete coverage of a job board.
- `PARTIAL` and `FAILED` are explicit; do not treat them as zero matching jobs.
- PostgreSQL 16 integration tests and .NET 10 xUnit run in GitHub Actions.

## Setup

Requires .NET 10 SDK and PostgreSQL. Create the DB/user in `scripts/init-db.sql` after replacing placeholder credentials. Never commit credentials.

```powershell
git clone https://github.com/avysotsky/JobRadar.git
cd JobRadar
$env:JOBRADAR_DB = "Host=localhost;Port=5432;Database=jobradar;Username=jobradar;Password=<your password>"
dotnet restore tests/JobRadar.Tests/JobRadar.Tests.csproj
dotnet test tests/JobRadar.Tests/JobRadar.Tests.csproj -c Release
dotnet run --project src/JobRadar/JobRadar.csproj -- --once
```

Long-running scheduled mode:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj
```

Configured times: 08:00, 13:00, and 19:00 Europe/Kyiv. JSON scan reports are written under `reports/`.

Diagnostics that do not require PostgreSQL:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-robota
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-dou
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-djinni
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-workua
```

The Work.ua command currently fails with HTTP 403 in GitHub Actions. It remains an explicit diagnostic, not a mandatory CI check.

## Compliance and limitations

Use official APIs/RSS when available and obey provider terms, throttling and quotas. No login or CAPTCHA bypassing. The Work.ua adapter is opt-in (set `EnabledWorkUa` to true only after access has been confirmed). Crawls are query-specific and cannot guarantee zero missed vacancies across an entire platform.

## Jooble — explicit manual mode

The free regional Jooble API key has 500 requests total over its lifetime (not monthly). Jooble is never invoked by the scheduled crawler. Requires your own Ukrainian Jooble regional API key and accurate historical usage:

```powershell
$env:JOOBLE_API_KEY = "<ua-regional-key>"
$env:JOOBLE_API_PRIOR_USED = "0"  # set ACTUAL historical lifetime calls, including those outside JobRadar
$env:JOOBLE_MAX_NEW_REQUESTS = "2" # new-call budget across all local runs
$env:JOBRADAR_DB = "Host=localhost;Port=5432;Database=jobradar;Username=jobradar;Password=<password>"
dotnet run --project src/JobRadar/JobRadar.csproj -- --jooble-once
```

Do not set historical usage to zero when unknown. Reservations occur atomically in PostgreSQL before network calls and remain consumed even if the request fails. Jooble results are **snippets only**, not verified full descriptions; no publication dates are inferred from the API's `updated` field. See [phase 3 design](docs/PHASE3_JOOBLE.md).
