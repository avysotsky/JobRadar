# Phase 19 — PostgreSQL-backed collector lease

## Problem observed in the first live operating cycle

On 11 October 2026, the user ran JobRadar from Windows against an existing
operational PostgreSQL database (\`jobradar_live\`): 328 references across 7
configured queries, 290 stored employment vacancies, 13 LikelyFit and 8
short-lived Upwork projects in the combined operator view.
The source crawl finished **PARTIAL/DEGRADED** (DOU and Djinni are feed-limited,
Robota full-text returned HTTP 403). Phase 18 was subsequently accepted with
**254/254 tests passing**, including PostgreSQL, but its local PowerShell mutex
protected only invocations of \`operate-now.ps1\`. A standalone \`dotnet --once\`
process, container, \`--retry-failed\`, opt-in Jooble command or built-in
scheduler could bypass that mutex and write concurrently.

## Solution

\`CollectorLease.TryAcquireAsync\` establishes a **dedicated, unpooled
PostgreSQL session** and executes \`pg_try_advisory_lock(bigint)\`. The session
stays open until the complete collector operation ends; a finally/async-dispose
path explicitly releases the lock and closes the physical session. A second
process targeting **the same PostgreSQL database** gets an immediate
nonblocking busy result and **must not fetch anything or write crawl data**.

Protected entry points in \`Program.cs\`:

- \`--once\`: full DOU/Djinni/Robota scan, DB writes and post-crawl retry.
- Built-in scheduled collector loop (08:00/13:00/19:00 Europe/Kyiv): each
  scheduled cycle tries to acquire the same lease; if busy, skip that cycle.
- \`--retry-failed\`: manual fetch-error recovery, separately acquires lease.
- \`--jooble-once\`: extra opt-in provider, lease held before **quota reservation**
  and HTTP request. Jooble is not automatically scheduled.
- \`scripts/operate-now.ps1 -RunCrawler\` interprets new busy exit **5** as
  **NO SCAN**, distinct from source-degraded exit **4**.

Pure DB status/triage/read-only operator views and synthetic offline Upwork
JSONL review do **not** acquire the lock and remain available during crawls.
The independent short-lived Upwork snapshot stays out of PostgreSQL. This
workstream does not install, turn on or enable any background Windows task,
GitHub Actions or Docker container.

### Exit semantics

| Exit code | Meaning |
|---|---|
| 0 | Operation finished with no configured report-level failures |
| 3 | PostgreSQL initialization unavailable |
| 4 | Source/record errors or incomplete fetch/retry as defined by the command |
| **5** | **Collector lease already owned**. No new crawl or Jooble quota reservation |

\`PARTIAL\` *coverage* is still faithfully reported, even when no individual
source failure is observed. The advisory lock is a best-effort concurrency
safety barrier for participating JobRadar processes; do not launch older
unpatched JobRadar binaries concurrently. A PostgreSQL network/session loss
releases the lock at the server and requires the process to fail/recover;
a subsequent workstream can add an explicit lease heartbeat if needed.
The lock is **database-scoped**, so using a different DB does not coordinate
with the original one. It does not solve legal provider permissions or enable
automatic Upwork MCP polling.

## Acceptance gates

**Latest workstream branch HEAD** must be checked locally; no GitHub Actions.

1. Set \`JOBRADAR_TEST_DB\` to the isolated **\`jobradar_test\`** database.
2. Run \`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-local.ps1 -RequirePostgres\`. Expected **257 tests** after the existing Phase 18 baseline 254 plus three database-backed lease tests; a skipped DB integration inside a green xUnit run is insufficient.
3. Optional two-process contention acceptance: deliberately hold a collector
   operation in one terminal, then from a second terminal run the collector
   against **the same operational database** and verify exit 5 **without
   issuing HTTP requests**; this requires owner supervision and must not be
   performed concurrently until the lock build passes.
4. A read-only operator overview remains functional during a collector scan.
5. Do not claim unattended 24/7 production operation merely from a successful
   lease test; host, Windows scheduling and secret handling require explicit
   deployment steps and separate acceptance.

## Delivery boundaries

- Independent stacked **Draft PR**, based on the already locally accepted
  Phase 18 PR #25 branch HEAD \`90a538855bf608d2793d69663630a5f6e909fdce\`.
- Phase 18 and Phase 19 must never be merged together without explicit
  owner authorization. Update Phase 19 base to \`main\` *after* Phase 18 merge.
- Do not re-enable GitHub Actions, add workflow files, spend Upwork Connects,
  automate applications or run full-text Robota HTTP requests against HTTP 403.
