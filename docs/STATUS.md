# JobRadar status — 2026-10-09

## Historical GitHub Actions verification (before disablement)

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

## Phase 4 (2026-10-09)

- Durable backlog recovery: `jobs` + `fetch_errors` persists discovered vacancies even if they leave the public RSS feed; `retry_runs` records recovery attempts.
- `--pending-status` (zero network requests) reports ready, deferred, blocked, exhausted jobs; `--retry-failed` replays a configurable bounded batch.
- HTTP 401/403/404 are not automatically retried; source authorization restrictions are respected. Jobs remain visible, not deleted.
- Source-level trend flag: >50% loss of unique references relative to an earlier nonfailed run with at least 20 references marks a query `PARTIAL` and emits `CoverageWarning`.
- Historical CI tested the PostgreSQL retry queue and coverage trend, including `--pending-status` against disposable PostgreSQL 16. CI is now disabled.
- Former GitHub Actions public-source diagnostics for DOU and Djinni are disabled and never constituted a persistent production crawl.
- **Still incomplete:** provider-side caps, full pagination beyond RSS, Work.ua access, real Jooble key, active/closed classification and operational deployment.

## Phase 5 — Robota paging verification

- Public API diagnostic on `менеджер`: 52,767 reported; pages 0,1,2 each supplied 59 unique IDs, no overlaps. This is a three-page sample, not an exhaustive scan.
- Probe on `backend`: 36 reported, 35 extracted, page 1 empty. The crawler now records RawRecords and DroppedRecords and marks queries PARTIAL on parse loss.
- Company JSON `totalVacanciesCount` is compared to filteredVacancies length; missing jobs in potentially truncated (100-item) responses remain unknown, not closed.
- Multi-page reconciliation, duplicate pages, raw record drops and company caps covered in xUnit and PostgreSQL tests.

## Phase 6: multi-query RSS, container deployment and full-text export — 2026-10-09

- Extra DOU C# public RSS search returned 25 references; extra Djinni C# RSS search returned 100. Both sources remain PARTIAL because the feed cap and remote filter accuracy are not exhaustively verified.
- Canonical per-site vacancy IDs and `job_queries` preserve search provenance without duplicating overlapping job records.
- `DetailRefreshHours` caching avoids repeated full-text network calls for recently read vacancies, with `CachedDetails` in source run reports.
- Added `--export-jsonl` with up to 2,000 records by default, including descriptions, query provenance and explicit HasFullText flag.
- Added multi-stage non-root .NET 10 Dockerfile and Docker Compose with persistent PostgreSQL and report volumes. PostgreSQL is not exposed on the host.
- Verified Docker Compose build, database startup, pending-status CLI and JSONL export in GitHub Actions with a disposable test password.
- GitHub Actions **does not** deploy this collector on the user's machine or provide persistent hosted storage; user-controlled Docker host/credentials are still required.
- Robota company feed may still cap at 100; Work.ua HTTP 403 remains unresolved, and no site-wide completeness guarantee exists.

## Phase 7 — remote-only .NET candidate triage (2026-10-09)

- Added deterministic .NET/C# matching with source-linked reasons for score, remote format (Remote/Hybrid/Onsite/Unknown), explicit exclusions, and warnings.
- CLI `--triage-jsonl` exports three independent views from PostgreSQL: high-confidence text-supported remote candidates, review-needed records, and complete audit (including excluded records). No network calls.
- Hybrid, explicit onsite and Senior/Lead titles cannot enter the remote shortlist. Unknown remote or preview-only remains review-needed.
- Spoken English and B2+ are warning signals, preserving candidates for manual inspection; other soft stack matches influence priority, not irreversible deletion.
- Null open status remains *unknown* and does not prove the vacancy currently accepts applications.
- No automatic application sending or actual remote/English fact verification; heuristic limitations are documented.

## Phase 8 — eligibility evidence (locally validated)

- New multilingual review gates for remote country restrictions, mandatory relocation, backend duties, and mandatory WPF/WinForms.
- Negated remote language and incomplete descriptions no longer silently produce some false-positive or false-negative classifications.
- Added 33 synthetic regression scenarios plus summary counters for high-scoring manual-review jobs, geo warnings, preview-only review, and unknown provider open status.
- **Validation state (2026-10-09):** user ran .NET SDK 10.0.401 Release build and **84/84 xUnit tests passed**, including real PostgreSQL 16 integration tests on a separate `jobradar_test` database at port 54321. Live-source completeness, actual remote eligibility, and production deployment remain unverified.
- **GitHub Actions emergency pause:** all six active workflows were atomically removed from main in a5a621b959836d8ea1d9400dfe220006a2876b9e. No PR, push or schedule jobs should be re-enabled without explicit consent.

See docs/PHASE8_ELIGIBILITY.md and scripts/validate-local.ps1. Source-grounded active/closed checks, deep coverage, and host deployment remain pending.

### Phase 8 continuation — provider status evidence and export omissions

- New nullable status checker infers **closed only from explicit provider-like HTML banner or short standalone closed page**, never from arbitrary description/footer text; it never infers open from an Apply button.
- Adds PostgreSQL `job_status_checks` immutable observation history, plus `jobs.status_checked_at` and `jobs.status_evidence`. Retry and crawl paths use the same observation logic. Unknown remains UNKNOWN; historical closure remains auditable.
- Protects status observations and description content against out-of-order fetch updates, without claiming an operational scan lease exists.
- Changes legacy `open_status=false` entries without current evidence to NeedsReview rather than silently excluding them.
- Adds `TotalStored` and `OmittedByLimit` to triage and raw JSONL export summaries; a capped export is not exhaustive.
- Adds synthetic status tests, PostgreSQL status-history/order tests and regression updates. **Local Release build and all 84/84 tests passed on 2026-10-09 with PostgreSQL integration enabled; no GitHub Actions were run.**
- Active workflow count remains zero by design. Do not restore GitHub Actions without user instruction.
