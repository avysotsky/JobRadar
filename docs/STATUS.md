# Phase 1 status

Prototype imported to GitHub on 2026-10-09.

- DOU: first page only; pagination is not implemented; coverage intentionally marked PARTIAL.
- Djinni: page-based URL and selectors are hypotheses pending validation on live responses.
- Robota.ua / Work.ua / Jooble: not implemented.
- Actual vacancy publication and active status: unverified.
- Automated tests cover small synthetic fixtures, not real page snapshots.
- GitHub Actions is configured to build and test .NET 10; do not claim passing CI until workflow result is checked.
- Never report zero matching jobs as exhaustive unless ingestion and coverage are validated.
