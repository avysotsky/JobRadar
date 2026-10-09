# Phase 5 — Robota.ua paging and capped company feed coverage

## Live evidence (9 October 2026)

- API: https://api.rabota.ua/vacancy/search uses zero-based page numbering with count=59.
- Query `менеджер`: total=52,767; pages 0,1,2 contained 59/59/59 distinct IDs, overlap=0.
- Query `backend`: total=36, first-page parsed IDs=35, page 1=0. This is a real ID-coverage discrepancy.
- Employer API: https://api.robota.ua/companies/{id}/published-vacancies returns filteredVacancies and totalVacanciesCount.

## Implemented safeguards

1. Compare raw JSON documents length to extracted IDs; report RawRecords and DroppedRecords.
2. Mark PARTIAL if any provider records cannot be converted into canonical vacancy URLs.
3. Distinguish reported total matching, deduped discovered IDs and full-text success.
4. Treat company feeds with at least 100 returned entries, or total greater than records received, as potentially truncated.
5. If a vacancy is absent from a limited company response, report unknown/truncated and retain the record; do not mark it closed.

## Commands

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --smoke-robota-pages
dotnet test tests/JobRadar.Tests/JobRadar.Tests.csproj -c Release
```

The probe is deliberately bounded at three public result-page requests; it neither fetches every result nor verifies job detail text. PostgreSQL fixture tests validate normal three-page reconciliation, overlapping pages, and lost IDs.

## Remaining gaps

- MaxPagesPerSource defaults to 12, so broad queries are deliberately incomplete.
- API reported totals may change between page requests.
- The company feed can be capped at 100; other permitted detail methods are needed for omitted IDs.
- Remote-only filtering and active/closed classification require independent validation.
