# Phase 2 coverage and provenance (9 October 2026)

## Goals and present scope

- `robota-api-<query>` identifies a query execution; canonical `jobs.source` is `robota`, regardless of which configured term discovered it.
- `job_queries` records every query that discovered a canonical vacancy. Unique vacancy IDs are not duplicated when a search term overlaps.
- `QUERY_RECONCILED` means **all ID references returned by the API for one configured query were accounted for**, and the stored job descriptions passed the minimum-length extraction check. This does not prove total coverage of Robota.ua, accuracy of descriptions, active status, or relevance to the user's profile.
- `PARTIAL` means an incomplete listing page, truncated company feed, unknown limits, or missing detail. `FAILED` means an unprocessed source.
- RSS without a published total cannot be certified exhaustive; DOU remains `PARTIAL`.
- Work.ua source is **disabled by default**. Public HTML extraction is speculative until successful live diagnostics and source terms verification. Never treat Work.ua 403 as zero vacancies.

## Diagnostics

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-dou
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-workua
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-robota
```

Robota details are read from `https://api.robota.ua/companies/{id}/published-vacancies`. The feed can be capped at 100; a missing advertised job remains a recorded detail error.

No login, CAPTCHA, bypass or aggressive scraping. Follow source terms and limits.

## Remaining priorities

1. Validate Work.ua source access and approved RSS or API feed in writing; enable after proven stable full descriptions.
2. DOU full pagination or count-reconciliation, and Djinni live checks.
3. For each configured search, compare source totals and IDs, track drift over time, and retry missing details.
4. Jooble key handling and quota, independent employer URL verification.
5. Filter jobs only after initial discovery/storage; report remote/English/SQL matching separately.
