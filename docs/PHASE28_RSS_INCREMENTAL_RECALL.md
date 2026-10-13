# Phase 28 — Incremental RSS recall for .NET / ASP.NET Core vacancies

## Objective

The DOU and Djinni feeds are capped search views. A single generic .NET
query (or its C# supplement) can omit postings discoverable with a more
specific phrase. Phase 28 adds an independent **ASP.NET Core** RSS search on
each provider, then measures marginal canonical vacancy IDs seen within
each saved run. This is a bounded coverage improvement, not a provider-wide
completeness guarantee.

## New default searches (on next normal collector run)

- DOU: existing .NET category feed; additional `C#` and `ASP.NET Core`
  keyword feeds.
- Djinni: existing .NET remote RSS; additional `C#` and `ASP.NET Core`
  keyword feeds.
- All sources preserve existing canonical provider keys (`dou`, `djinni`)
  and independent query provenance (`job_queries`); repeated job URLs do
  not create duplicate jobs, and recent full-text results remain cached.
- The additional sources add **two RSS requests per normal collector
  run**, plus ordinary provider detail requests for previously unseen
  vacancies. Existing delay and retry controls apply. No HTML scraping,
  authentication, pagination claims or 403 bypass are introduced.
- These options live in the tracked `src/JobRadar/appsettings.json`. The
  owner can disable either keyword through their local deployment config.

## Audit command

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --rss-query-recall
```

The command reads the latest persisted crawl report and deduplicated
`job_discoveries` observations between its `StartedAt` and `EndedAt`
for exactly the RSS query names present in the saved report. It does not
fetch or contact provider sites, schedule scans, or write vacancy data.
Like other DB operator commands, program startup calls the existing
`Storage.Initialize` (which can create missing schema on a new database).

Output fields for each ordered query:

- `ReportedReferences`: unique references in that query's crawl result.
- `ObservedDistinctUrls`: canonical URLs present in persisted discovery
  events during the report time window.
- `IncrementalUrls`: URLs not seen in *earlier* RSS queries of that same
  provider in this run. This is order-dependent attribution, not the
  provider's entire catalog or all jobs posted on that date.
- `AlreadySeenInEarlierQueries`: URLs overlapping earlier queries.
- `MatchesSavedCrawl`: whether event cardinality matches report reference
  count, with a non-failed fetched source.
- `IncrementalProviderTotals`: union of discovered URLs across configured
  queries within a provider, **null** if any contributing query's attribution
  is unverified.

If the event count does not reconcile with the saved query result, the
unverified query and all later queries from the same provider receive
`INCREMENTAL_ATTRIBUTION_UNVERIFIED` and their incremental counts remain
`null`. The CLI returns exit 4 for incomplete or empty data and exit 0
when the report's included RSS queries reconcile.

Observation timestamps do not provide a transaction ID, so concurrent
collectors using the same query name can contaminate the observation window.
Cardinality reconciliation detects many such cases but cannot prove
single-run isolation; the production collector should run under one
owner-controlled lease when one is enabled. Source-level RSS status remains
PARTIAL even if the audit's stored event counts match perfectly.

## Synthetic illustration

For DOU queries in order:

| Query | Observed URLs | Incremental | Already seen |
|---|---:|---:|---:|
| .NET default | A, B | 2 | 0 |
| C# | B, C | 1 | 1 |
| ASP.NET Core | C, D | 1 | 1 |

This yields 4 unique stored DOU URLs rather than 2 in the first feed.
The example does **not** assert this will happen on any real provider:
only future permitted crawls and saved evidence can measure a gain.

## Guardrails and acceptance

- PostgreSQL discovery rows are read using bounded `DISTINCT` with
  a `LIMIT + 1` overflow guard (maximum 50,000 unique rows).
- A failure, missing events or a count mismatch does not become a
  fabricated zero or a claim of extra market coverage.
- No schema change, new secret, job-application automation, changed
  running processes, GitHub Actions or Relay infrastructure work.
- HP Trusted Git Relay v2 accepts only immutable exact-SHA
  `restore → build → test` results, all with cleanup 5/5.
- No live provider calls are made by the HP acceptance tests.

## Next verification

Compare actual collector run output after deployment, not synthetic fixtures.
If the new `ASP.NET Core` queries contribute zero marginal IDs across
several otherwise valid runs, remove them or evaluate more relevant
search terms based on the stored query evidence. Do not claim 100%
coverage for capped RSS.
