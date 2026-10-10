# Phase 11 — Vacancy matching accuracy

## Scope

Improve the deterministic triage rules using observed **classes of mistakes** in the owner's 265-record local audit (22 likely, 93 review, 150 excluded before this change). No actual private vacancy description or copied provider page is committed. Synthetic xUnit cases test patterns without relying on source availability.

This change adjusts only candidate classification; it does **not** fetch jobs, delete records, prove vacancy availability, apply to companies, alter SQL schemas, or override source access restrictions.

## Changes

1. **Mid/Senior is mixed, not Senior-only.** Titles such as `Mid-Senior`, `Mid/Senior`, `Mid to Senior` continue to receive human review. Clear Senior/Lead-only titles remain Excluded under existing policy.
2. **Senior role stated inside a generic title's description.** Phrases such as `looking for a Senior Engineer`, `As a Senior .NET Engineer`, `Position: Senior Developer` trigger `NeedsReview`. Mere mentions of senior colleagues or instructions to `lead design` do not establish Senior role ownership.
3. **Frontend/fullstack and mobile/desktop specialization.** A FullStack/React/Frontend title, explicit mandatory React/Angular/TypeScript frontend work, or .NET MAUI/Xamarin/WPF specialization in the title is a review signal even with `.NET` and API keywords. Optional frontend references are scrubbed locally before detecting mandatory ones, reducing false positives.
4. **English level in either word order.** `Upper-Intermediate English`, `English: Upper Intermediate`, `Advanced English`, and B2/C1/C2 in either order now prompt manual review. Written English B1 remains eligible for likely.
5. **Remote or hybrid alternatives.** `remote or hybrid` without a clear attendance requirement means Unknown/NeedsReview; explicit office days or mandatory office attendance still cause Hybrid/Excluded. No loosening of hard remote constraints.

**Conservative gates:** potential matches are routed to review, not silently discarded. Reasons, warnings, existing scores and known status evidence remain visible. These remain regex heuristics, not independent verification of an employer's requirements.

## Test/validation

Added self-contained synthetic regression scenarios for mixed titles, senior body language, misleading incidental senior terms, frontend/mobile requirements, B2 English word order, optional React, ambiguous hybrid choice, and mandatory onsite override.

Before changes: **91/91 passed locally with actual PostgreSQL 16 on .NET 10**, GitHub Actions off. The Phase 11 branch needs local validation with `JOBRADAR_TEST_DB` and `scripts/validate-local.ps1 -RequirePostgres`. No CI/GitHub Actions may be enabled or triggered without explicit authorization. An offline export on the real `jobradar_live` database will provide before/after class counts after validation, but individual vacancies still require manual checking.

## Known limits and next validation

- Only role-specific statements are considered for Senior in body; a novel wording may remain undetected.
- Mandatory vs optional frontend wording in natural language can be contradictory. Treat ambiguous employers as human-review cases.
- The work-mode detector is conservative; conditional or regional remote options may still require human review.
- Inspect the real 265-vacancy audit after this change for score and bucket drift. Count gains are not proof of higher precision without manually labeled ground truth.
