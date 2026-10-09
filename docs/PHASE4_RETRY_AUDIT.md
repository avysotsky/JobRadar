# Phase 4 — persistent missing-detail retry and coverage drift

## Failure mode addressed

Previously, a vacancy ID could be discovered and recorded in PostgreSQL, but a detail-page error left `description` empty. If the posting disappeared from an RSS feed on subsequent days, the crawler had no reason to retry. JobRadar now independently scans its persistent backlog to recover these records.

## Data model

- `jobs`: canonical source URL and metadata retained independent of listing/feed freshness
- `fetch_errors`: per-source-and-URL error, count and timestamp (upsert on successive failures)
- `retry_runs`: separate durable JSON reports of retry attempts and backlog before/after
- `crawl_runs`: searchable chronological source counts used to detect sudden regressions
- `job_queries`: discovery query provenance preserved

No backlog entries are automatically deleted when attempts are exhausted.

## Eligibility

The backlog contains source jobs with `full_text_at IS NULL` from enabled sources: Robota.ua, DOU, Djinni, optionally Work.ua only if explicitly enabled. Jooble is excluded because it currently supplies previews but no verified full description. All entries remain counted.

Eligible jobs exclude (but still count) known nonretryable HTTP 401, 403, 404 errors. A retry is deferred until at least `PendingMinimumAgeMinutes` have elapsed from the last attempt. `PendingMaxAttempts` limits retry attempts, and `PendingBatchSize` limits work per run. These are *not* a guarantee that a transient outage will resolve.

## CLI

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --pending-status
dotnet run --project src/JobRadar/JobRadar.csproj -- --retry-failed
dotnet run --project src/JobRadar/JobRadar.csproj -- --once
```

- `--pending-status` reads PostgreSQL only; no job board requests.
- `--retry-failed` performs bounded replay, saves `retry_runs`, exits nonzero if any selected attempt failed.
- `--once` crawls configured sources then, by default, retries eligible old missing descriptions. Nonzero exit means an operational or observed source failure.
- `PendingCounts`: `Total`, `Ready`, `Deferred`, `Blocked`, `Exhausted` are disjoint categories. Reports after a retry may put newly failed jobs in `Deferred`.

## Query coverage and alarms

- The crawler reads the last 10 stored `crawl_runs`, chooses the latest nonfailed reference count for each matching source.
- A 50%+ drop from a prior count of >=20 sets `CoverageWarning` and `PARTIAL`, even if the provider's reported total reconciles.
- This catches suspicious source regressions; it does not establish actual coverage completeness. A site can consistently return a capped RSS feed and never trigger this rule.

## Verification

xUnit/PostgreSQL integration tests check that missing full-text jobs are recovered, HTTP 403 remains visible but not retried, exhausted jobs remain countable, and a saved run becomes a coverage baseline. The GitHub Actions pipeline also executes the pending-status CLI against a disposable PostgreSQL 16 instance. Source smoke checks for Djinni/DOU are small daily samples, not a production data feed.

## Unresolved

- User-owned persistent PostgreSQL deployment and secure operations
- Source-wide pagination and cap reconciliation
- Recovery workflow for company feeds capped at 100 vacancies
- Authorized Work.ua access
- Jooble full source descriptions and actual account key, if any
- Vacancy status verification and remote/English matching
