# Phase 21 — Explicit employment application ledger (Draft)

Base: accepted Phase 20 SHA 9485961315447c92c6e218b724a52d2cc1fac4c5.
Branch: jobradar/phase21-employment-application-tracking.
**GitHub Actions OFF. No merge, no Task Scheduler registration, no application submission.**

## Why

Employment applications require a separate private tracking ledger. Employment
tracking is **different** from the existing manual freelance project tracker.
Never publish actual owner application details, employer names, vacancy IDs,
application dates, CVs, correspondence or personal job-search history.

## Semantics

- Two new PostgreSQL tables: employment_tracker and employment_tracker_events.
- Records are created **only through explicit local CLI owner commands**.
  No job source feed or parser creates application status, no remote write.
- Supported sources: https URLs on jobs.dou.ua, djinni.co, robota.ua,
  weworkremotely.com and remotive.com, with approved vacancy paths.
  Canonicalization strips query strings and fragments; restricts external hosts.
- Status begins at Saved. Owner can record Applied once (unique filtered index);
  Replied, Interview, Offer, Rejected and Withdrawn require prior Applied.
  Closed may be entered without Applied to suppress obsolete results.
- Own notes are limited and not printed by CLI or the unified preview.
- Full audit and DB vacancy counts remain intact. Unified preview overlays
  owner status separately from source open-status evidence.
  `--opportunities-actionable` hides tracked application outcomes (not Saved).
- A manually tracked reference absent from scraped jobs still appears in the
  tracker list, without inventing a provider vacancy or counting it as a scan hit.
- The employment tracker does NOT submit CVs, make HTTP requests, verify
  that a vacancy is open, or automatically register any status.

## Local validation on Windows (OWNER ONLY)

Use the development checkout, **not** D:\projects\JobRadar-ops.
Confirm the exact Phase 21 PR HEAD before executing.

Set JOBRADAR_TEST_DB privately for the isolated jobradar_test database,
not jobradar_live. Then:

```powershell
Set-Location 'D:\projects\jobradar'
git fetch origin
git switch jobradar/phase21-employment-application-tracking
git pull --ff-only origin jobradar/phase21-employment-application-tracking
$env:DOTNET_ROOT = 'D:\DotNet'
$env:PATH = 'D:\DotNet;' + $env:PATH
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-local.ps1 -RequirePostgres
```

Expected baseline: 257 existing tests + 14 new xUnit cases = 271 tests.
**Not yet validated on owner Windows; no passing result is claimed.**
The test DB must be isolated and its password never posted to chat.
Do not run the new tracking commands against live DB until acceptance.

## Owner-only operational adoption after tests and explicit decision

Only after local acceptance, run manual tracking commands on the private
owner-controlled machine, using privately entered vacancy URLs and labels.
Never copy real application commands, real vacancy identifiers, recruiter
correspondence or owner data into a public issue, PR, commit, test or runbook.

CLI operations:
- `--employment-track-add --employment-url=<PRIVATE_URL> --employment-label=<PRIVATE_LABEL>`
- `--employment-track-event --employment-url=<PRIVATE_URL> --employment-event=Applied`
- `--employment-track-list`
- `--opportunities-preview --opportunities-actionable`

Use the approved development or deployment version; keep the pinned Phase 20
operational checkout unchanged. Do not rescan providers just to record
local manual actions.

## Exit/coverage caveats

The Phase 20 operator script throws on DEGRADED coverage, causing the
PowerShell process to exit 1 even though its collector returned 4 and triage
completed. Phase 21 changes this operator exit to 4 after reports finish;
source status remains partial and must not be presented as full coverage.

## Acceptance criteria

- Confirm test suite passes on the owner's Windows machine with PostgreSQL enabled.
- Positive and negative URL validation, duplicate Applied rejection, manual
  outcomes and actionable hiding tested.
- Stable pinned Phase 20 ops SHA unchanged.
- No scheduler, GitHub Actions, automatic bids or applications.
