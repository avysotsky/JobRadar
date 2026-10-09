# Phase 8 — Remote eligibility evidence and false-negative review

## Scope

This change strengthens Phase 7 deterministic triage without new network calls, bypassing access controls, deleting vacancies, or claiming that a job is still active.

A remote-work phrase is **not proof that a candidate in Ukraine can work remotely**. Confident ranking now requires explicit remote mode, captured full text, .NET plus backend/API evidence and no known eligibility gates. Ambiguities remain in NeedsReview, not silently discarded.

## Evidence gates

- **Location**: wording like "remote within EU", "must be based in Germany", "EU residents only", or "excluding Ukraine" => NeedsReview. A reference to EU time zone alone is not a country restriction.
- **Relocation**: "relocation is required", "обов'язковий переїзд", "обязательный переезд" => NeedsReview; optional relocation support is not a blocker.
- **Backend**: a general .NET position without backend/API/server-side evidence => NeedsReview, even with a high score.
- **Desktop**: WPF/WinForms core title or mandatory WPF => NeedsReview; optional WPF does not force review.
- **Remote negation**: "remote work is not available", "Віддалена робота неможлива", "Удаленная работа невозможна" => Onsite. Office/hybrid restrictions continue to outrank isolated remote keywords.
- **Incomplete previews**: a short teaser without C#/.NET is insufficient proof of irrelevance => NeedsReview. A complete, unrelated Java-only job remains Excluded but visible in the full audit.
- Spoken English B2+, mixed Middle/Senior, closed status and unknown work mode retain Phase 7 behavior.

These are conservative heuristic review prompts, **not a legal or provider-verified eligibility determination**. They cannot reliably understand every quoted, conditional, negated, historical, or multilingual statement.

## Audit counters

The triage JSON summary now includes HighScoreNeedsReview, GeoRestrictedNeedsReview, IncompleteTextNeedsReview and UnverifiedOpenStatus. These categories overlap and must not be summed as unique vacancies. LastSeen is discovery evidence, not proof the job is active. Unknown provider status remains unknown even for a recently discovered job.

## Regression corpus

The synthetic multilingual regression JSON contains 32 scenarios covering UA and foreign-country restrictions, time zones, relocation, hybrid cloud vs office, remote-team mentions, English, backend vs desktop, preview-only jobs, mixed seniority, and unrelated stacks.

Local offline test command (GitHub Actions are disabled):

~~~powershell
.\scripts\validate-local.ps1
# For PostgreSQL tests, set JOBRADAR_TEST_DB and require it:
.\scripts\validate-local.ps1 -RequirePostgres
~~~

If JOBRADAR_TEST_DB is absent, the repository's existing database tests skip their integration work. Do not claim they were exercised merely because the xUnit process exits zero.

## Remaining Phase 8 work

- Introduce provider-grounded status checks, checked-at timestamps and evidence origin (UNKNOWN where unsupported).
- Compare rankings against manually labeled collected real jobs stored privately, never in this public repository.
- Add DB-wide scan lease, host deployment validation, and authorized deeper source pagination.
- Quantify measured precision and recall before asserting production quality gains.

## CI spending stop

Six active workflows were atomically removed from main in commit a5a621b959836d8ea1d9400dfe220006a2876b9e on 2026-10-09. Restore only on explicit owner's instruction. Do not label PR checks green when they have not run.
