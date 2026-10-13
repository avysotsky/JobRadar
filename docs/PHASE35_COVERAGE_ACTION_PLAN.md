# Phase 35 — Offline coverage action plan for Junior+/Middle .NET Backend search

## Purpose

Phase 27 (--coverage-review) identifies scan-history anomalies; Phase 34
(--discovery-gaps) reconciles the latest provider-query counts with saved
discovery events; Phase 33 (--vacancy-funnel) shows whether stored jobs have
enough evidence to recommend or review. Phase 35 joins those **existing
signals** into a deterministic, prioritized list of **human investigation
steps**, never autonomous website access.

## Command

    dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --coverage-action-plan

Uses the existing owner-configured PostgreSQL connection (JOBRADAR_DB).
The normal application startup may initialize its database schema as before.
The plan itself only reads the latest 20 crawl reports, at most 50,000
deduplicated discovery occurrences from the most recent crawl window, a
bounded stored-vacancy snapshot (configured ExportMaxRecords, clamped to
1–50,000), total stored rows and explicit Applied/Inactive decisions.

No provider HTTP requests, access workarounds, 403 detail retries,
configuration writes, status changes, applications, notifications, new
background jobs, GitHub Actions or changes to source quotas occur. The
report contains action codes, priorities and evidence; it does not
output individual job URLs, descriptions or application history.

## Representative actions

| Code | Scope | Investigation |
| --- | --- | --- |
| SOURCE_FETCH_FAILED (P0) | Query | Inspect connector failure and permissions; no bypass |
| DISCOVERY_COUNT_MISMATCH (P1) | Query | Compare reported unique IDs with saved discovery events |
| SOURCE_REFERENCE_LOSS (P1) | Query | Check invalid references/duplicates using saved fixtures |
| ROBOTA_SEARCH_PAGING_UNVERIFIED (P1) | Query | Inspect reported-total gap/page overlap/total drift |
| EMPTY_FEED_UNVERIFIED (P1) | Query | Inspect RSS parser and saved empty-feed evidence |
| OBSERVED_DISCOVERY_DROP (P1) | Query | Compare historical samples; market change is possible |
| QUERY_NOT_IN_LATEST_SCAN (P2) | Query | Review enabled queries and cadence; not zero vacancies |
| QUERY_ADDS_NO_MARGINAL_URLS (P2) | Query | Compare valid overlapping RSS results across multiple runs |
| ROBOTA_DETAIL_RESTRICTED (P2) | Query | Keep blocked full-text endpoint paused; use permitted previews |
| REVIEW_TARGET_VACANCIES (P2) | Provider | Manually verify target .NET jobs and Ukraine eligibility |
| REVIEW_PREVIEW_ONLY (P2) | Provider | Manually inspect preview-only records within provider permissions |
| STALE_COLLECTOR_EVIDENCE (P1) | Overall | Check whether owner-authorized collector should run |
| TRUNCATED_VACANCY_AUDIT (P1) | Overall | Stored-vacancy sample limited; consider local bound adjustment |

P0/P1 items need attention. Exit code **4** when any P0/P1 is present;
otherwise exit **0**. With no crawl data, NO_SAVED_CRAWL (P1) is explicit;
no provider is assumed to have published zero vacancies. A stale newest
collector run is flagged only after 48 hours.

## Attribution and safety limitations

- Source-name (DOU/Djinni/Robota/WWR) funnel counts are **provider totals**.
  They do not reveal which keyword found a job. Provider-wide target
  reviews have Query: null and never attribute a vacancy to a keyword feed.
- The report ties the latest-run discovery audit to the **exact same
  EndedAt timestamp** as the newest historical crawl report. A mismatch
  fails closed instead of combining unrelated data.
- A mismatched query taints subsequent same-provider marginal results.
  Never turn unverified null counts into a recommendation to delete a
  query. Even a verified zero from one run only triggers investigation.
- Provider totals, capped RSS and historical reference counts do not
  establish market-wide recall, current employer applications, remote
  Ukraine eligibility or mandatory English B2/C1 speaking.
- PreviewOnly, NeedsReview and StatusUnverified are separate aggregates.
  Their intersections are **not** inferred.
- An incomplete ExportMaxRecords sample is visibly marked
  (VacanciesOmitted > 0; CompleteVacancySample: false).
  Concurrent collectors can contaminate time-window observations.
  These reports are not a transactionally isolated snapshot.

## Regression and acceptance

Offline tests cover failed and omitted sources, raw-reference loss,
Robota paging, capped RSS without an error, zero-marginal keyword feeds,
stale reports, incomplete vacancy sampling, restricted Robota details,
canonical provider-only advice, and refusal to merge unrelated crawl
windows. A PostgreSQL-backed test uses saved crawl_runs, job_discoveries,
jobs and employment_decisions.

The installed WSL2 Draft PR watcher validates the exact HEAD via sequential
restore → build → test. The immutable result is stored in PRIVATE:

    avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/pr-<N>/<FULL_SHA>.json

Acceptance requires exact SHA and Git tree, overall=PASS, restore/build/test
PASS, nonzero tests with zero failures, and all **six** cleanup flags true:
database, role, runner, secret, snapshot, workspace. No new tasks are
published to the historical JobRadar tasks-v2 queue.
