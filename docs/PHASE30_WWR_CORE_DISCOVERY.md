# Phase 30 — We Work Remotely RSS in the core JobRadar collector

## Purpose

Expand the regular employment **`--once`** search beyond DOU, Djinni and Robota.ua.
Two publicly documented We Work Remotely (WWR) RSS categories are added by default
in the checked-in `appsettings.json`:

- `wwr-backend` — `https://weworkremotely.com/categories/remote-back-end-programming-jobs.rss`
- `wwr-programming` — `https://weworkremotely.com/categories/remote-programming-jobs.rss`

The official WWR feed directory authorizes using these public RSS URLs with
attribution back to We Work Remotely:
https://weworkremotely.com/remote-job-rss-feed

The owner can disable both via `"EnabledWwr": false` in local config.
No new auth tokens, daily provider keys, scheduler changes or GitHub Actions.

## Integration and data policy

- These are single-page/capped RSS views; each source report remains `PARTIAL`
  even on an otherwise successful scan.
- Both feed variants map to the canonical job source `wwr`;
  `job_queries` retain `wwr-backend` / `wwr-programming` independently.
  A repeated URL across the two feeds produces **one** job row.
- Each original WWR URL is stored as a backlink to its original listing.
  No application path bypasses WWR; this tool never auto-applies.
- The stored preview is a **bounded 950-character RSS excerpt**.
  Do not store or claim a complete description from RSS or perform
  per-vacancy detail-page HTTP requests: the regular crawler intentionally
  bypasses the detail-fetch path for `WwrRssSource`.
- All WWR openings have `IsOpen=null` until independently verified.
  The Phase 29 recommendation gate keeps unverified and preview-only
  openings in `NeedsReview`, not the actionable `LikelyFit` shortlist.
- "Remote" does **not** establish eligibility to work from Ukraine.
  The triage warnings explicitly require country and active-status review.
- Preserve RSS raw/unusable/duplicate counts and provenance. Do not assert
  completeness when a feed is capped or contains discarded entries.
- Existing `--remote-boards-once` remains a separate manual preview
  for WWR+Remotive, with its own independent scoring and attribution.
  It is **not** invoked as a side effect of `--once`.

## Provider constraints and next phase

- Work.ua: HTTP 403, remains disabled; never bypass access controls.
- Jooble: API key and already-declared lifetime budget needed; not silently
  enabled or billed.
- Remotive: API guidance recommends at most 4 queries/day and original
  Remotive attribution, with a delay in published API results:
  https://remotive.com/remote-jobs/api
  Remotive is not enabled in `--once` until persistent daily request
  accounting and provider-specific privacy/attribution limits are validated.

## Local verification after merge

With the owner's normal PostgreSQL connection set:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --once
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --triage-jsonl
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --coverage-review
```

Expected: new `wwr-backend` / `wwr-programming` rows in crawl report,
`wwr` vacancies in the PostgreSQL-based triage audit and review queue.
The source can fail due to networking/feed availability; a failure must
remain visible in the run report and does not certify zero vacancies.

## Acceptance

Mock-only HTTP tests verify category URLs, canonical identity, unsafe
source URL rejection, duplicate/loss accounting and that the crawler
never fetches WWR individual pages. PostgreSQL integration verifies
per-query provenance and conservative, review-only classification.

HP Trusted Git Relay v2 on one exact SHA:
`restore` → `build` → `test` with PASS, exit 0, 5/5 cleanup.
No live provider calls in acceptance.
