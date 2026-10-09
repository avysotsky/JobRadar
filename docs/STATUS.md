# Implementation status — 2026-10-09

## Verified
- .NET 10 restore/build and xUnit passed on GitHub Actions; PostgreSQL 16 service tests exercise persistence.
- Robota.ua public search JSON returned 56 discovered records for one `.net` query, with a reported total of 56 in a live test.
- Robota.ua company published-vacancies JSON supplied a full body for publicly posted Credit Agricole vacancy `11287397`. End-to-end smoke extracted **9,835 readable characters** and date **2026-10-05**.
- Robota API source persists discovered metadata and preview before attempting full text; query totals and failures are reported.
- Robota company feed downloaded once per company per crawl, in-memory cached.

## Not proven / remaining
- No exhaustive all-queries/all-sites vacancy coverage guarantee.
- A company feed can have a 100-record cap or omit a searched vacancy. Such detail requests fail explicitly; they are **not** marked complete.
- Search API paging, query parameter semantics and filtering need broad live validation on different queries, cities and page counts.
- DOU RSS may be truncated; no independent exhaustive count reconciliation.
- Djinni HTML selectors and paging have not been validated against live site responses.
- Work.ua, Jooble and further sources not yet integrated.
- Full-text completeness beyond the tested Robota fixture, deduplication across keyword queries/sources, and independent active-vacancy verification remain to implement.
- Integration tests use a disposable PostgreSQL 16 service in GitHub Actions; local Windows and Linux environments not verified.
- Source statuses `FAILED`, `PARTIAL` and `UNVERIFIED_COVERAGE` are **not** claims of exhaustive completeness.

## Controls
- Failed full text stays in `fetch_errors`; metadata stays in `jobs` (without `full_text_at`).
- HTTP 403/404 are not retried; HTTP 429 respects bounded Retry-After; source errors are not silently converted into zero jobs.
- No CAPTCHA or login bypassing; use publicly accessible information and respect source restrictions.
