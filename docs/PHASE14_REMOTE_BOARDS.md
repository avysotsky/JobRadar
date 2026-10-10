# Phase 14 — Remote international boards: WWR RSS + Remotive public API

## Source contract

This is a **manual, read-only** source adapter with no API keys, no PostgreSQL writes and no changes to the scheduled `--once` crawler.

Official providers:

- [We Work Remotely public RSS](https://weworkremotely.com/remote-job-rss-feed), categories:
  - `https://weworkremotely.com/categories/remote-back-end-programming-jobs.rss`
  - `https://weworkremotely.com/categories/remote-programming-jobs.rss`
- [Remotive public jobs API](https://remotive.com/remote-jobs/api), documented endpoint `https://remotive.com/api/remote-jobs?category=software-dev&limit=250`.

WWR grants use of public RSS and requires **attribution linking to We Work Remotely**. Remotive requires direct attribution and backlinks to its original job URL, and prohibits reposting jobs to third-party job aggregators. Remotive results **may be delayed at least 24 hours**. Its public API guidance advises **no more than four calls per day**, and throttles excessive requests.

The code does not repost listings to third-party platforms. All results retain original provider URLs and an explicit `Attribution` field. Each command performs **two WWR feed requests and one Remotive API request**, with at least 1.2s spacing. This is a local, manual command; do not automate frequent repeated runs.

## Usage (no PostgreSQL required)

```powershell
cd D:\projects\jobradar
$env:DOTNET_ROOT = 'D:\dotnet'
$env:PATH = 'D:\dotnet;' + $env:PATH
dotnet run --project src/jobradar/jobradar.csproj -c Release -- --remote-boards-once
```

JSONL lines are written to stdout; counts, errors and coverage to stderr. Never treat missing RSS results as proof of no available openings; feeds are capped and Remotive's `limit` is 250.

## Matching and safety

- Deduplicate identical canonical WWR URLs across the backend and all-programming feeds (without changing attribution).
- Preserve every parseable item in the output, including `Excluded`; raw and dropped record counts are explicit.
- Use the existing Middle .NET triage scoring and warnings; **WWR RSS items are preview-only** until a full description can be validated, so they remain `NeedsReview` rather than falsely `LikelyFit`.
- Remotive API returns full HTML descriptions. The scanner evaluates **the entire received text in memory** before classification, but writes only **up to 2400 characters** in its JSONL excerpt. The full text is marked non-serializable and is not persisted; requirements after the excerpt still affect risk warnings.
- `candidate_required_location` for Remotive is essential: only exact `Worldwide`, `Anywhere`, `Global`, `Ukraine` or `Ukraine only` is allowed to remain `LikelyFit`. Wording such as `Worldwide except Ukraine`, `Europe, Ukraine` or `Ukraine not eligible` remains ambiguous and goes to manual review. Missing restrictions, `Europe`, `USA only` and other ambiguous locations prompt review. No automatic claim of legal employment eligibility.
- Publication timestamps lacking timezone offsets are interpreted as UTC rather than the local machine timezone. Remote does not imply geography-free employment. `IsOpen` is never claimed verified based on feed presence. The timestamp comes from the feed/API and may reflect aggregator delay.
- URLs must be HTTPS on the exact provider host, avoiding malicious redirect links; HTTP redirects disabled.
- If one provider fails (e.g. 429/403), the other results are preserved, the source status is FAILED and the command returns non-zero; no automatic retry or CAPTCHA bypass. Coverage stays PARTIAL even when every configured feed responds.

## Boundaries and next work

This slice deliberately **does not yet persist remote openings to PostgreSQL**, add scheduled runs, invoke extra full-text HTML scraping, or claim comprehensive international coverage. It tests the provider formats and ranking with synthetic fixtures. A later production integration may add DB-backed source-specific status/retry tracking after one verified lawful live run and explicit user approval.

No GitHub Actions workflows are created or enabled. Validate offline with `scripts/validate-local.ps1 -RequirePostgres` pointing to the isolated `jobradar_test` database; this exercises all existing integration tests but does not invoke the real boards.
