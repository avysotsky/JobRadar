# JobRadar status — 2026-10-09

## Verified by GitHub Actions

- .NET 10 restore, build, tests, and PostgreSQL 16 integration tests pass.
- PostgreSQL creation uses a transaction-scoped advisory lock to avoid parallel-test schema creation races.
- Canonical Robota source IDs and query-to-vacancy provenance are tested against PostgreSQL.
- Robota.ua public JSON API returned 56 entries for .NET and matched its reported total in a smoke test.
- Robota.ua Credit Agricole vacancy `11287397`: 9,835 extracted readable characters, date 2026-10-05.
- DOU RSS: 25 entries returned in live diagnostic; one full vacancy description extracted (4,625 characters). **DOU feed is not necessarily exhaustive.**
- Work.ua public search URL: live HTTP 403 from GitHub Actions. The parser is disabled by default. **Not a verified data source.**

## Coverage semantics

- `QUERY_RECONCILED`: extracted ID count matches the source's explicit total for one query, with full-text fetch attempts completed successfully.
- `PARTIAL`: page truncation, missing full text, ambiguous source totals, or other incomplete run.
- `FAILED`: no usable page from that query/source.
- `UNVERIFIED_COVERAGE`: content obtained but exhaustive pagination not proven.

Even a `QUERY_RECONCILED` result is not evidence of complete coverage of the platform. Companies may cap their published-vacancies feed at 100 items; such missing details are not silently ignored.

## Next

- Verify and expand Djinni.
- Find a permissioned Work.ua feed/API or user-authorized access method; do not attempt to bypass 403.
- Add Jooble regional API with a guarded lifetime key quota (500 total free calls).
- Broaden search queries and report completeness per query, plus active/closed status verification.
- Avoid scheduled production runs until coverage/error metrics and host setup have been validated.

## Phase 3 (2026-10-09)

- Djinni: public filtered RSS verified live — 54 items and one 1,891-character detail. Exhaustiveness and filter accuracy remain UNVERIFIED.
- Jooble: official Ukrainian regional API client implemented, with explicit one-shot command and PostgreSQL lifetime quota reservations. No live API calls without user key and prior usage declaration.
- Jooble produces search previews, not complete descriptions; all Jooble runs remain PARTIAL_SNIPPET_ONLY.
- Work.ua remains HTTP 403 and disabled by default. No bypass.
