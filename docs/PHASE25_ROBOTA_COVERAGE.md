# Phase 25 — Robota source-coverage evidence (offline / PostgreSQL)

## Purpose

The existing `SourceResult.Status` contract (`FAILED`, `PARTIAL`,
`QUERY_RECONCILED`) remains unchanged for old consumers. A previous
`DroppedRecords` count may combine malformed provider rows and duplicate
IDs inside one page. It is **not** a count of jobs proven missing.

Phase 25 adds a read-only, derived machine-readable view of the last
persisted Robota query reports. It neither fetches live provider pages
nor changes the discovery/query-provenance or retry tables.

## Operator command

Use an already configured isolated or operational PostgreSQL database:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --robota-coverage
```

`JOBRADAR_DB` is resolved by the existing JobRadar startup path. This command
never requests Robota, DOU, Djinni or any other provider. It reads the most
recent saved `crawl_runs` JSON report and outputs only source-level counts,
statuses and diagnostic codes, not vacancy descriptions or personal exports.
It does not create a new crawl; startup still calls the standard schema
initializer. Return code 4 means no report, no Robota source in the latest
report, or an inability to read the report. A printed report is NOT a
fresh/current source scan.

For each Robota query, the output includes:

- `RawRecords`: provider rows received, if reported by the saved run.
- `DroppedWithinPages`: prior ingestion count (invalid **or** repeated
  within a page); it does not independently isolate both causes.
- `UniqueReferences`: canonical distinct IDs discovered by that query.
- `ReportedTotal`: the first provider-advertised search total, or null.
- `UnobservedFromReportedTotal`: `max(0, ReportedTotal-UniqueReferences)`,
  null if total unknown. This is a **difference against one total snapshot**,
  not evidence that a specific missing job exists or was closed.
- `Signals`: sorted, additive cause/evidence codes. A query may have more
  than one simultaneous signal.

## Important signal distinctions

| Signal | Meaning |
|---|---|
| `VERIFIED_ZERO_RESULTS` | First page says total 0, contains zero raw rows, crawl reconciled |
| `EMPTY_FIRST_PAGE_UNVERIFIED` | First page has no usable IDs but no verified empty proof |
| `EMPTY_PAGE_BEFORE_TOTAL` | A later page is empty before successful reconciliation |
| `SEARCH_FETCH_FAILED` | Search-page HTTP/retrieval error; not a zero-result query |
| `SEARCH_PARSE_FAILED` | JSON/parser error after receiving a response |
| `RAW_RECORDS_DROPPED` | Some raw rows were invalid or duplicated within the page |
| `CROSS_PAGE_OVERLAP` | Previously seen IDs reappeared on later query pages |
| `REPORTED_TOTAL_DRIFT` | Reported total changed, disappeared or first appeared later |
| `REPORTED_TOTAL_UNKNOWN` | No usable first-page provider total |
| `UNOBSERVED_FROM_REPORTED_TOTAL` | First-page total is larger than observed unique IDs |
| `UNIQUE_IDS_EXCEED_REPORTED_TOTAL` | Observed unique IDs exceed first-page reported total |

Codes are evidence about a **single query**. They never imply 100% coverage
of Robota.ua, active vacancy status, Ukraine eligibility or permission to
retry HTTP 403 endpoints. A legitimate zero remains distinct from both
an HTTP 403 and malformed payload.

## Compatibility and acceptance

- No new PostgreSQL tables or dependencies.
- Old persisted reports missing `RawRecords` retain null (unknown) and
  must never be retroactively reclassified as verified zero.
- Original `crawl_runs`, `job_queries`, `fetch_errors` and full-text
  restrictions remain the source of truth.
- The new synthetic tests use an isolated disposable PostgreSQL database
  and stubbed HTTP responses, no real provider requests.
- Remote HP acceptance must be confirmed by Trusted Git Relay v2
  `restore`, `build`, `test` on the exact source commit.
