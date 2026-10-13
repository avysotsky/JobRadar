# Phase 31 — review queue ordering for Middle / Junior+ .NET Backend

## Problem
After Phase 29 correctly downgrades openings without explicit positive provider status
to NeedsReview, the review JSONL can be large. Ordering that file only by the
technical-match score can promote difficult Middle/Senior, mandatory C1, WPF or
FullStack roles ahead of substantially better matches.

## Change
- Keeps the existing FitBucket, status evidence, full-text and record retention.
- Adds a **separate** `VacancyReviewTier` used only for sorting `NeedsReview`:
  1. `TargetDotNetBackend`: .NET/C# plus Backend/API evidence.
  2. `PartialDotNetEvidence`: .NET is present, Backend is not established.
  3. `DifficultRequirements`: senior ownership (including Ukrainian/Russian
     senior titles), explicit high English proficiency, restricted remote countries,
     required relocation, office or hybrid.
  4. `OtherSpecialization`: WPF/Avalonia/desktop, frontend, FullStack or no .NET.
- Within a tier, descending existing score and last discovery time still apply.
- Distinct precedence prioritizes an explicit UI/FullStack specialization over
  senior restrictions; both are below target backend opportunities.
- No vacancy is declared open or closed by this tier. `IsOpen=null`
  remains unverified. An incomplete Robota preview may still appear high
  in the review queue when its title/preview clearly names .NET Backend.
- This is a **manual-review ordering**, not an automated send/apply decision.
  Existing application suppression from Phase 29 remains intact.

## Acceptance
Synthetic xUnit coverage for level, English, specialization and previews.
HP Trusted Git Relay v2 restore/build/test on exact SHA with five-resource cleanup;
no external job-site requests in test acceptance.

## Mandatory handoff reference
All future handoffs must include the mandatory autonomy-first Relay policy:
`docs/orchestration/AUTONOMOUS_RELAY_AND_HANDOFF_POLICY.md`.
