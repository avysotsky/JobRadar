# JobRadar

.NET 10 vacancy ingestion for Ukrainian software development jobs — [GitHub repository](https://github.com/avysotsky/JobRadar).

**Status:** locally validated ingestion prototype with Robota.ua, DOU and Djinni sources, supplemental RSS search queries, PostgreSQL persistence, optional Docker deployment and full-text JSONL export. This is **not** an exhaustive crawler; read the [coverage status](docs/STATUS.md) and [coverage design](docs/PHASE2_COVERAGE.md).

## GitHub Actions spending protection (2026-10-09)

**All six GitHub Actions workflows are disabled** (removed atomically in [commit a5a621b](https://github.com/avysotsky/JobRadar/commit/a5a621b959836d8ea1d9400dfe220006a2876b9e)) due to the user's explicit instruction. Historical GitHub CI results below describe past runs only; they are not evidence of any new validation. **Do not restore workflows, automatic PR checks, scheduled jobs, or workflow_dispatch without explicit permission.** Use [local test validation](scripts/validate-local.ps1) instead.

Phase 8 adds conservative geographical and role-eligibility review gates, multilingual synthetic regression cases, and review-backlog metrics. See [Phase 8 design](docs/PHASE8_ELIGIBILITY.md). The Phase 8 branch passed the owner's local Release build and all **84/84 xUnit tests**, including PostgreSQL 16 integration tests against an isolated `jobradar_test` database on 2026-10-09. These results do not validate live provider coverage or deployment.

## Sources and evidence

| Site | Discovery | Full-text | Evidence |
| --- | --- | --- | --- |
| Robota.ua | Public JSON search API | Public company published-vacancies JSON | Real .NET search, total reconciliation, and Credit Agricole full vacancy retrieved in GitHub Actions |
| DOU | RSS | Public vacancy HTML | Default RSS and supplemental C# RSS verified live; capped to 25 references per tested feed |
| Djinni | Public RSS | Public vacancy HTML | Default feed found 54, supplemental C# RSS found 100; coverage unverified |
| Work.ua | Draft HTML search, **disabled by default** | Draft HTML | GitHub runner returned HTTP 403; do not bypass access controls |
| Jooble | Official regional REST API, opt-in only | Search snippets only | Client, key protection and PostgreSQL quota guard tested; no live key used |

## Design

- Store discovered vacancies **before** attempting their full descriptions.
- Canonical IDs avoid duplicate Robota listings across several queries (for example `.net` and `backend`).
- `job_queries` preserves the keyword query provenance of every discovered vacancy. Different DOU/Djinni RSS queries share canonical vacancy IDs.
- Recently saved full descriptions are reused for overlapping queries, with CachedDetails reported per source.
- `fetch_errors` tracks unreadable descriptions and source/network errors. **Even jobs no longer present in a feed are retained and retried.**
- `retry_runs` records attempts to recover older missing full texts. `--pending-status` displays backlog without network traffic.
- Raw Robota JSON record counts are compared to parsed vacancy IDs; dropped entries cause PARTIAL with DroppedRecords metrics.
- `QUERY_RECONCILED` indicates matching the API's total for **one query**, not complete coverage of a job board.
- `PARTIAL` and `FAILED` are explicit; do not treat them as zero matching jobs.
- On 2026-10-09, .NET 10 Release build and **84/84 xUnit tests** passed locally, with PostgreSQL 16 integration tests enabled. GitHub Actions remain disabled.

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
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-robota-pages
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-dou
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-djinni
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-workua
```

The last GitHub-hosted Work.ua probe received HTTP 403; Actions are now disabled. The Work.ua adapter remains an optional diagnostic and is not a verified source.

## Live progress in Windows PowerShell

The normal `--once` crawl prints and flushes timestamped progress **while it is running**:
source/query number, page fetch, listings returned, full descriptions saved, cached details, failures and final source status. Detail counts update after the first job and every five discovered jobs. The bounded pending-detail retry worker also reports per-item recovery counts.

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --once
```

Progress does not include individual vacancy URLs, descriptions or credentials. Query-level source totals may be missing or inaccurate; **no site-wide percentage or ETA is claimed**. The existing final JSON report is still written and returned after the crawl, including partial/failed coverage. GitHub Actions remain disabled.

## Robota.ua full-text access pause (2026-10-09)

An actual Windows crawl returned HTTP **403 Forbidden** for the Robota.ua company published-vacancies detail endpoint. The search/index endpoint succeeded, but full-text requests failed for every one of the 85 unique Robota vacancies. This is an **access failure, not proof of a closed vacancy**.

The packaged `appsettings.json` now uses `"EnabledRobotaDetails": false`. Robota search queries **remain enabled** and continue storing IDs, URLs, titles, company names, available search previews and query provenance; reports remain `PARTIAL`. The collector and pending-detail retry worker **do not call the refused detail endpoint** while this flag is false, including after restart. The `--pending-status` command still includes unresolved Robota jobs, and pre-existing `fetch_errors` are preserved for audit.

Only set `EnabledRobotaDetails` to `true` after confirming authorized provider access to that endpoint or a documented provider-approved replacement. Do not bypass 403, forge credentials, or interpret a preview as the complete job description. You can contact the official provider support/partnership channels to clarify access. Public search coverage and full description coverage are separate metrics.

## Full-text recovery and coverage audit

A job discovered today but absent from tomorrow's RSS remains in PostgreSQL. The retry worker selects jobs lacking full text, independent of current source results. It uses bounded retries (default: 20 jobs per run, minimum 6 hours between attempts, maximum 5 attempts per job). Only enabled, supported sources are retried; **Jooble previews are not treated as complete vacancy descriptions and are excluded**.

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --pending-status
dotnet run --project src/JobRadar/JobRadar.csproj -- --retry-failed
```

`--pending-status` makes **zero HTTP calls** and shows `Total`, `Ready`, `Deferred`, `Blocked`, and `Exhausted`. HTTP 401/403/404 jobs are retained but not automatically retried; exhausted jobs also remain visible for manual investigation. Retrying is enabled after each normal crawl and can be disabled via `RetryAfterCrawl=false` in appsettings. Structured retry reports persist in `retry_runs`.

Each source report now compares the current number of unique vacancy references with a recent previously saved run. A drop below half the prior count (when the prior count is at least 20) adds a `CoverageWarning` and marks that source `PARTIAL` even if the API says the query is complete. This is a **suspicion of coverage loss**, not proof of an error. Normal RSS caps and missing keyword categories still prevent exhaustive guarantees.

A single `--once` run now exits nonzero when a source fails, descriptions fail or a coverage anomaly is detected; JSON crawl reports are saved first. Historical GitHub Actions source smoke tests verified only sampled source pages. GitHub Actions are now disabled; no unattended source scans or persistent crawler are active.

See [phase 4 technical notes](docs/PHASE4_RETRY_AUDIT.md).

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

## Robota.ua pagination coverage

Live paging diagnostic for broad query **менеджер**: 52,767 total reported; three distinct pages of 59, 177 unique vacancy IDs, no overlap. A `backend` probe reported 36 matches but only 35 parseable IDs. Such mismatches are not treated as complete results. The company published-vacancies JSON can return 100 records while reporting more vacancies; absence from that list is not evidence that an ad is closed. See [Phase 5 notes](docs/PHASE5_ROBOTA_PAGINATION.md).

## Persistent Docker deployment (Windows / Linux)

Requires Docker Compose v2 and a running user's machine/server. **No permanent collector has been deployed to your machine by GitHub Actions.**

```powershell
Copy-Item .env.example .env
# Edit .env and set your own strong, secret JOBRADAR_POSTGRES_PASSWORD
docker compose up --build -d
docker compose logs -f collector
```

PostgreSQL is on an internal Compose network (no published host port). Named volumes persist database and reports across container upgrades. Scans run at 08:00, 13:00 and 19:00 Europe/Kyiv.

```powershell
docker compose run --rm collector --pending-status
docker compose run --rm collector --export-jsonl
docker compose cp collector:/app/reports ./jobradar-reports
```

The JSONL export includes full vacancy descriptions, unknown/known text completeness, publication dates, titles, URLs, and query provenance; exported files can be attached to ChatGPT for analysis. Keep them private as appropriate and never commit environment secrets.

See [Docker runbook](docs/DEPLOY_DOCKER.md) and [Phase 6 technical notes](docs/PHASE6_MULTI_FEEDS.md).

## Multiple RSS searches and text cache

The normal crawler now includes an extra DOU C# RSS search and an extra Djinni C# RSS search in addition to the original .NET RSS feeds. These filters were verified with public GitHub Actions probes. Configure `DouExtraKeywords` and `DjinniExtraKeywords` to expand the search set; feeds may cap their results, so source status is always PARTIAL.

Already downloaded descriptions are reused across overlapping queries for up to `DetailRefreshHours` (24 by default). `CachedDetails` counts these reused records; `DetailsFetched` counts actual fresh retrievals. The database still records each query separately.

Manual standalone output without Docker:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --export-jsonl
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-feed-variants
```

The export is bounded at `ExportMaxRecords` (default 2,000) and may not include all historical records without changing that setting. It does not assert that vacancies are still active.

## Middle .NET remote shortlist (Phase 7)

The optional local rules-based triage produces **three JSONL files** from already stored vacancies. It performs zero job-board HTTP requests and makes no claims that an advertisement is currently active.

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --triage-jsonl
# Docker:
docker compose run --rm collector --triage-jsonl
```

- `triage-remote-likely-*.jsonl`: strong .NET matches with explicitly described remote work and a full description. Not a booking or employment guarantee.
- `triage-review-*.jsonl`: unknown/ambiguous work mode, optional English requirements, insufficient description, or mixed seniority; these still need manual investigation.
- `triage-all-*.jsonl`: every evaluated vacancy, including explicit hybrid/office, Senior/Lead, unrelated stacks, or independently flagged closed postings. Each record contains a score, matching signals, warnings, and the source URL.

**Remote is a hard constraint**: explicit office attendance or hybrid schedules are excluded from the shortlist. If remote is not confirmed, the vacancy goes to human review, not to the confident recommendations. English B2+/spoken requirements are warning signals requiring checking, rather than silent exclusions. Full-text missing and active status unknown remain visible.

Rules-based screening is intentionally conservative and can make errors, especially with multilingual advertisements or negation. Never automatically submit applications or delete records based on this ranking. Review source descriptions before responding. [Detailed decision rules](docs/PHASE7_TRIAGE.md).
