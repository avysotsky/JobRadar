# Phase 34 — Four-provider stored discovery gap audit

## Objective

Show which configured vacancy search queries lost records during collection,
which have mismatches between saved crawl counts and discovery events in
PostgreSQL, and which are intrinsically not exhaustive. This extends offline
diagnostics to **DOU, Djinni, Robota and We Work Remotely** (WWR).

**No stored report can prove the complete inventory of a public provider.**
"Unobserved" refers only to IDs implied by an explicit Robota search-query
reported total, **not** a verified list of missing job URLs. RSS absence is
never an assertion that a vacancy does not exist or has closed.

## Operator command

    dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --discovery-gaps

Reads the newest persisted crawl_runs report and the corresponding
job_discoveries time window (started_at through ended_at). Requires
the existing JOBRADAR_DB or configured PostgreSQL connection.

The command does NOT run a crawl, change settings, contact providers, retry
HTTP 403, fetch vacancy details, update status timestamps, mark applications,
consume Jooble/Remotive requests, or change existing jobs. The normal startup
schema initialization remains unchanged.

It reads up to 50,000 **distinct (source, query, URL)** events using a
LIMIT 50001 guard. Overflow or malformed report returns exit 4 rather than
hiding rows. SQL only considers whitelisted canonical providers dou,
djinni, robota, wwr, and query names present in the saved report.

## Fields and priorities

Each query has:
- ReportedReferences: unique references claimed by that query's saved run.
- ObservedDistinctUrls: distinct canonical saved discovery events with the
  same query name, correct provider and report time window.
- MatchesStoredDiscoveries: valid page fetch and exact cardinality match.
  If another collector wrote events within the same query/time window,
  attribution may still be contaminated; this is a consistency check, not a
  transactionally isolated crawl.
- NewToEarlierQueries and OverlapWithEarlierQueries: unique/overlap
  counts **relative to preceding queries of the same provider in that run**,
  not newly advertised vacancies or site-wide recall. Counts become null
  if the current or any earlier same-provider query cannot be reconciled.
- UnobservedFromReportedTotal: max(0, ReportedTotal - ReferencesFound)
  for Robota *only* when a total exists. Never sum distinct Robota-query
  totals into a market-wide total.
- DroppedRecords: saved raw-record losses, null if not recorded historically.
- Signals: stable evidence strings for fetch failures, discarded items,
  mismatched events, RSS caps, Robota paging/total issues.

Priority:
- P0: saved source could not establish a successful fetched page;
- P1: saved event-count mismatch, raw record loss, detail failure, provider
  reported-total gap or explicit RSS/Robota source error;
- P2: otherwise consistent capped RSS or nonreconciled Robota coverage;
- P3: a reconciled Robota query without these observed issues.

PerProviderObservedUniqueUrls sums marginal counts in the saved query
order only if every included query for that provider reconciles. Otherwise
it is null for that provider; unrelated providers retain their values.

The CLI returns exit 4 for empty/no report or any P0/P1 row, 0 otherwise.
P2 never asserts a site is exhaustive. A provider mentioned in old scans
but absent from the latest run is omitted, **not treated as zero**.

## Relationship to existing reports

- --coverage-review: 20-run historical drift and prioritization. Phase 34
  adds the **WWR Backend and Programming RSS feeds** to this report with
  the same non-exhaustive warning as DOU/Djinni.
- --rss-query-recall: DOU/Djinni incremental RSS query attribution, unchanged.
- --vacancy-funnel: saved-vacancy actionability and owner Applied/Inactive,
  unchanged.
- --discovery-gaps: latest-run four-provider raw-to-persisted reconciliation.

Together they help target source defects without pretending that machine Score,
HTTP 200, a feed item or an explicit Robota total proves a currently open,
Ukraine-eligible Junior+/Middle .NET Backend position.

## Testing / security

Synthetic unit cases cover all four providers, incremental overlap,
cross-provider contamination, failed sources, RSS cap vs lost items, unknown
and reported totals, missing events and tainted attribution. PostgreSQL tests
use ephemeral test-only DB credentials and assert distinct event reads and
row bounds. No real vacancy HTTP calls are made by unit/integration tests.

The WSL2 watcher automatically tests the new Draft PR HEAD:
restore → build → test, with exact results in
avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/pr-N/<SHA>.json.
Verify all stages, nonzero passing tests, no failures, and cleanup 6/6.
Do not create tasks-v2 files. GitHub Actions remain disabled. Provider
request budgets and known 403 restrictions are unchanged.
