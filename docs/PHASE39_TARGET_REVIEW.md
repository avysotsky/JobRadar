# Phase 39 — operator target review (offline)

## Purpose

The October 14 live collector saved 549 vacancies and reported 37 high-score NeedsReview, but a high raw score can still belong to a FullStack React or Senior vacancy. Phase 39 provides a **separate bounded operator queue** based on the existing `VacancyReviewPriority` role/requirements tier rather than blindly sorting by score.

## Usage

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --target-review
```

This mode reads the existing PostgreSQL snapshots and manually recorded employment decisions, prints JSON to stdout and does not crawl job boards, refresh status, modify decisions, or submit applications.

Top 30 entries include source, original URL, title, company, review tier and reason, current fit score, work-mode evidence, full-text availability, local last-seen timestamp, historic status indicator, reasons and warnings. Every row requires a known backend-related review tier or a partial .NET indicator. Senior-only, core React/FullStack/WPF, location restrictions, mandatory high English and definite onsite/hybrid requirements are excluded via the existing gates. Applied/Inactive marked URLs are suppressed via existing canonical identity, without asserting employer closure.

Sorting: target .NET Backend first, partial .NET second, explicit remote ahead of unverified work mode, complete job text ahead of preview, then freshness, score, source and URL.

The `OpeningEvidence` marker distinguishes `OPEN_UNVERIFIED`, `POSITIVE_STATUS_RECORDED_WITHIN_24H` and `HISTORICAL_POSITIVE_STATUS_STALE` (with an explicit warning). Even a recent stored positive observation is timestamped evidence, not proof of live application acceptance. The report is not deduplicated across different boards based solely on a similar title; such merging requires verified canonical employer identity. The report shows total stored, examined and omitted-by-snapshot-limit; exit code 4 on truncated input, else 0. The displayed 30-item cap is separate from DB snapshot omission.

## Acceptance

Synthetic regression cases cover React/Senior false positives, owner decisions, rank ordering, remote vs unknown mode, limits, advanced English, location and office restrictions, and stale historical positive provider status. Only the established automatic WSL2 Draft-PR watcher may certify restore/build/test; inspect private `avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/pr-<PR>/<HEAD>.json`, exact repo/PR/SHA/tree match, nonzero passed, zero failed, no skipped, cleanup 6/6. No GitHub Actions, tasks-v2 retries, or bypass of host trust checks.

As of implementation, the previous Phase 38 PR #48 watcher suffered three consecutive pre-restore `INFRASTRUCTURE_ERROR` reports; issue #49 is open. This phase's code is **not** accepted until its own exact-head PASS.
