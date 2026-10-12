# Phase 27 — Offline multi-source coverage review

## Scope

JobRadar can now compare the last **20 persisted crawl runs** and identify
where observed DOU, Djinni and Robota queries deserve investigation.
It is an operator-facing triage report, **not** a claim that all vacancies
on any provider were seen.

## Command

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --coverage-review
```

The command uses the existing `JOBRADAR_DB` / local configuration and reads
only the PostgreSQL `crawl_runs` table, ordered by insertion ID. It produces
JSON with `EvaluatedAt`, `ReportsExamined`, `NewestReportAt`, and a
per-query `Sources` array. No HTTP requests, crawler runs, changes to
source settings, migrations, notification schedules, GitHub Actions,
permissions or trading/contractor services are triggered.

With no reports or no DOU/Djinni/Robota queries, the JSON response is
explicitly empty and the command returns exit code 4. Malformed stored
reports fail closed rather than disappearing from the audit.

## Priority levels

| Priority | Evidence in the sampled reports | Recommended investigation |
|---|---|---|
| P0 | Latest observed source status is `FAILED` | Check source connectivity, HTTP errors or parser failure before other investigations |
| P1 | Raw records discarded, Robota total gap/drift/overlap, empty unverified response, failed details or sharp observed-count decline | Inspect the specific query and captured failures; verify provider contract, paging and parsing |
| P2 | No evidence of P0/P1 but source absent from newest run, last observation older than 48 hours, or normal non-exhaustive RSS / other unverified status | Check configuration, run cadence and RSS cap |
| P3 | No detected issue in the sampled Robota query beyond known limitations | No immediate action on this evidence alone |

Sources are ordered P0 → P3, then provider and query name.
`LatestReferences` is the latest **observed** unique reference count,
not a vacancy market total.

## Independent evidence and caveats

- `NOT_IN_NEWEST_RUN` does **not** mean zero vacancies. The source may be
  disabled, omitted from a scan or covered by a different configuration.
- `OBSERVED_REFERENCES_DOWN_OVER_50_PERCENT` is emitted only when the
  previous non-failed peak in this bounded window is at least 10 and the
  latest observed count is below half. A changing job market can also
  explain this, so it is a flag to investigate, not a proven ingestion loss.
- `PriorObservedPeak` excludes failed runs; it is a historical observation
  and cannot establish provider-wide coverage.
- `UnobservedFromFirstTotal` is calculated only when the saved query has
  a reported total. It does not identify particular missing vacancies or
  infer that any vacancy has closed.
- `RawRecords` and `DroppedRecords` stay null for historical runs that
  did not record those metrics. Unknown must not be interpreted as zero.
- RSS sources are intentionally non-exhaustive even when all returned
  items are valid; a routine `PARTIAL` alone is not treated as a source
  outage.
- Robota full text is still paused in configured environments where the
  company-details endpoint returned HTTP 403; this report does not retry
  it or bypass provider restrictions.
- Queries excluded from the most recent scan are reported using their last
  available observation within the bounded history, never fabricated as
  freshly scanned results.
- Because this analyzes saved runs, any conclusions about **today's**
  coverage require fresh, explicitly permitted crawl data in that database.

## Regression acceptance

Offline unit cases cover source failure ranking, RSS capping vs actual item
loss, missing queries, prior peaks, stale observations, true zero vs unknown
raw counts, Robota gaps, and old duplicated report rows.
A PostgreSQL-backed test checks persistence and bounded history loading.
HP Trusted Git Relay v2 acceptance must use the exact PR source SHA,
`restore → build → test`, and report full 5/5 cleanup.

## Next work

Use the JSON audit on the actual JobRadar collector's retained history to
identify specific high-priority queries. Prioritize missing provider listings
and under-covered query families using an authorized live sampling method.
Avoid broadening scraping or enabling autonomous applications merely to
increase a headline vacancy count.
