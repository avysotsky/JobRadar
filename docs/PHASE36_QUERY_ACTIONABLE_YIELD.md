# Phase 36 — Measured query-level yield of .NET Backend candidates

## Objective

Measure which already-configured search queries contribute distinct vacancy
URLs whose **current stored evidence** qualifies for Junior+/Middle .NET
Backend review. The score is retrospective, not evidence that a vacancy was
open or eligible when originally discovered.

No additional provider HTTP calls, query changes, relaunches, automated
applications or status updates are introduced.

## Offline CLI

    dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --query-actionable-yield

Use the existing owner-controlled PostgreSQL connection. The normal
application startup can initialize its schema as before. This command reads
the most recent crawl report, the matching bounded job_discoveries time
window (hard limit 50,000), current stored vacancy snapshots (bounded
by ExportMaxRecords, max 50,000), total DB row count and the
user's own Applied/Inactive decisions. No provider requests or data writes
occur within the new audit.

## Meaning of fields

- ObservedUrls: unique canonical URLs recorded for that query in the run.
- MatchedCurrentSnapshots: number of these URLs present in the currently
  loaded vacancy snapshot, including overlaps.
- MarginalUrls: URLs not already seen in **preceding queries for the
  same provider**, in the original crawl order.
- CurrentLikelyFit: marginal jobs whose **current** stored evidence passes
  existing recommendation gates, including recorded open-status evidence.
- CurrentTargetBackendReview: marginal jobs currently in NeedsReview with
  Phase 31 top-tier target .NET Backend evidence.
- CurrentPartialDotNetReview: other .NET review candidates without a
  confirmed backend specialization.
- CurrentOtherReview: more difficult/other review categories.
- CurrentExcluded: explicitly excluded by existing fit/user-decision gates.
- CurrentStatusUnknown: overlapping diagnostic for marginal jobs with
  insufficient positive status evidence; it is NOT a closure count.
- CurrentTargetBackendMarginalByProvider: valid summed top-tier target-review
  candidates for each provider; null if any component query is unverified.
- CurrentSnapshotOmitted: total stored jobs absent from the bounded snapshot.

For every verified query, the five exclusive current fit buckets above sum
to MarginalUrls; CurrentStatusUnknown is an additional overlapping count.

## Data-quality guarantees

Query identity comes from job_discoveries observations in the actual crawl
window. Never infer original provenance solely from all-time job_queries
records. Canonical source and URL form a vacancy identity. DOU, Djinni,
Robota and WWR attribution are isolated. Same-provider duplicates count once
for the first matching query in that stored run.

A query emits NULL rather than fabricated zero if its event count cannot be
reconciled, an earlier same-provider query is unverified, or any incremental
event lacks a current stored vacancy. Incomplete snapshot status is explicit;
historical updates to vacancy text or decisions may change this report
without changing the underlying discovery events.

Returns exit 4 for no usable crawl, unverified query or truncated current
sample, and zero otherwise. Even a clean exit does not prove that the board's
published listings were exhaustively discovered or currently open.

## Use for measurable improvements

Compare identical search keywords across **multiple** clean authorized runs.
A query repeatedly contributing new target-review URLs is potentially
useful. A query repeatedly producing only overlap can be reviewed for
redundancy. Neither one clean run nor a synthetic fixture justifies
automatically disabling a query or enlarging provider request volume.
RSS category feeds are capped; Robota totals cover individual searches;
working from Ukraine and spoken-English requirements need manual review.

## Regression and WSL2 acceptance

Pure unit tests cover overlap across queries, cross-provider separation,
manual Applied exclusions, unknown statuses, missing current snapshots,
earlier query count mismatches, zero marginal counts and invalid windows.
PostgreSQL regression covers actual persisted query provenance and owner
decisions. Acceptance is offline: NO network crawling.

Draft PR HEAD triggers the installed WSL2 watcher restore, build and test.
Read immutable result from PRIVATE
avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/pr-N/FULL_SHA.json.
Require exact SHA, tree, all three PASS, tests passed>0 and failed=0,
cleanup database/role/runner/secret/snapshot/workspace all true.
Do not publish JobRadar tasks-v2 or enable GitHub Actions.
