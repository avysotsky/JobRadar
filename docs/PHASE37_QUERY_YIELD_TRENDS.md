# Phase 37 — Bounded multi-run query yield trends

## Objective

Compare existing saved searches over **up to six consecutive recent collector runs** without adding HTTP requests, changing feed keywords, or manufacturing provider-wide vacancy coverage claims.

The phase extends Phase 36's per-run retrospective matching of canonical discovery URLs with the **current** stored .NET Backend assessment.

## Offline command

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --query-yield-trend
```

The command reads at most six stored `crawl_runs` (newest insertion first), a single bounded current vacancy snapshot (configured `ExportMaxRecords`, maximum 50,000), the saved user `Applied/Inactive` decisions, and at most 50,000 distinct discovery observations per saved run. There is also a hard **100,000 total observation cap** across all runs.

No provider HTTP requests, new or re-ordered keywords, retry work, database updates to jobs/decisions, schedules, automatic applications or GitHub Actions are introduced. Existing normal startup schema initialization still applies.

## Validity conditions

A cross-run assessment is published only when **all** conditions hold:

1. At least **three** runs exist.
2. Runs are ordered newest-first and have **non-overlapping inclusive** discovery windows.
3. Each compared provider uses the **same set and order** of query identifiers across all runs; changing the order can change which query receives marginal attribution.
4. Each Phase 36 query count reconciles with saved discovery events, including verified earlier same-provider query attribution.
5. No current vacancy snapshots necessary for the query are missing and the saved-vacancy snapshot is complete for that run.

A failed condition marks the affected provider/query comparison `UNVERIFIED` with **null** trend totals, never zero. Fewer than three clean runs produce `INSUFFICIENT_RUNS`. A run included in the recent window but not containing the newest provider query does **not** become a zero-result scan.

## Metrics per query

- `RunsExamined` and `VerifiedRuns`: bounded number of reports and successfully comparable measurements.
- `SumMarginalUrls`: sum of Phase 36 marginal URLs **within** each run (repeated URLs may count again on subsequent days).
- `SumCurrentTargetBackendReviews`: sum of those per-run marginal URLs classified in the **current** top-priority .NET Backend `NeedsReview` tier.
- `RunsWithNewUrls`: number of runs in which a query produced at least one marginal canonical URL.
- `RunsWithTargetReviews`: number of runs with at least one currently matching target-review URL.

These are **not unique URLs across the entire period** and not historical snapshots of employer availability. Current classification and Applied/Inactive decisions can change after each crawl.

`Assessment` values:

| Value | Meaning |
| --- | --- |
| `REVIEW_RETAIN_TARGET_QUERY` | Target-review evidence present in at least two of three or more comparable runs |
| `REVIEW_ZERO_MARGINAL_QUERY` | Zero marginal URLs in all comparable runs; human audit only |
| `REVIEW_LOW_TARGET_YIELD` | Some additional URLs, but none currently in target backend review |
| `MIXED_TARGET_YIELD` | Intermittent current target signal; no stable conclusion |
| `INSUFFICIENT_RUNS` | Fewer than three clean runs |
| `UNVERIFIED` | Changed query order/set, overlapping windows, truncation or upstream uncertainty |

No assessment is an instruction to change keywords automatically, increase crawler request volumes, or disable provider feeds. A `REVIEW_ZERO_MARGINAL_QUERY` finding from capped RSS cannot prove that the underlying provider lacks additional jobs.

## Exit status and operational interpretation

The command prints JSON and returns exit **4** when no comparable query rows exist or any row is `UNVERIFIED` or `INSUFFICIENT_RUNS`. Otherwise it returns exit **0**. A healthy exit means the **saved evidence is internally comparable**, not that any vacancy is currently open or eligible for remote work from Ukraine.

Unknown regional eligibility, required spoken English, source closures and stale provider status remain separate human review gates.

## Acceptance

Unit tests cover clean three-run trends, partial evidence, repeated overlap, evolving keyword sets, query reordering, independent providers, empty snapshots, reversed chronology, and upper bounds. PostgreSQL-backed integration test constructs three independent stored crawl windows and verifies complete query-yield analysis against actual `job_discoveries`.

A Draft PR new HEAD SHA triggers the existing WSL2 watcher `restore → build → test`. Exact immutable results live in PRIVATE `avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/pr-<PR>/<SHA>.json`. Verify repository, PR, commit SHA, Git tree, all stages PASS, nonzero passed, zero failed, and six cleanup flags. Historical `tasks-v2/` is not used. GitHub Actions remain disabled.
