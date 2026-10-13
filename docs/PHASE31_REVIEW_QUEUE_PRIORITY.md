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

## Acceptance (WSL2 Draft-PR watcher, 2026-10-13)
Synthetic xUnit coverage for level, English, specialization, preview-only
records and remote-country restrictions (including "Remote EU only" and
"EU-only remote"). No external job-site requests during test acceptance.

The WSL2 service detects each new HEAD of this Draft PR and runs
`restore → build → test` for
`tests/JobRadar.Tests/JobRadar.Tests.csproj`. It publishes one immutable
version-1 result for the exact HEAD SHA at
`avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/pr-41/<FULL_SHA>.json`.
Merge requires `overall=PASS`, all three stages `PASS`, real test counts
with zero failures, and all six cleanup flags `true` (database, role,
runner, secret, snapshot, workspace). Missing or older-SHA results never
qualify a new HEAD. A code fix must use a new commit/SHA.

**Do not publish JobRadar tasks to `tasks-v2/` anymore.**
The previous manual JobRadar Relay task-queue process is superseded by the
WSL2 Draft-PR watcher. TradeOps/DocFlow queues are unaffected.

## Mandatory handoff reference
All future handoffs must include the mandatory autonomy-first Relay policy:
`docs/orchestration/AUTONOMOUS_RELAY_AND_HANDOFF_POLICY.md`.
