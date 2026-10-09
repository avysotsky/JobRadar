# Phase 1 status (updated 2026-10-09)

- DOU: uses advertised public RSS endpoint with .NET/remote query. RSS may be truncated. Marked PARTIAL until reconciled with total count. RSS parsing has synthetic fixture tests.
- Djinni: explicit .NET remote search with numbered pages. Selectors still require live validation; query and extraction coverage are not guaranteed.
- Robota.ua: public-search adapter and detail extraction are implemented as a draft; query pagination, HTML selectors and full-text quality have synthetic fixture tests only and are not live-validated.\n- Work.ua / Jooble: not implemented.
- Source-level report statuses: FAILED, PARTIAL, UNVERIFIED_COVERAGE. No status currently guarantees exhaustive indexing.
- Parser and storage workflows must be exercised against real page snapshots and a PostgreSQL test instance.
- .NET 10 CI configured; passing build and tests not confirmed.
- HTTP 403/429 and parse failures must be surfaced rather than represented as no vacancies.
