# Phase 32 — bounded, manual DOU/Djinni status-only verification

## Motivation

The full-text cache can prevent repeated HTML fetches after subsequent RSS
discoveries. An updated `last_seen` must NOT be interpreted as a fresh provider
open-status check. Phase 29 correctly keeps `IsOpen=null` out of the high
confidence shortlist. This phase provides a separate bounded, auditable
way to re-check explicit closures without altering vacancy text/freshness.

## Usage

Run only after the normal `JOBRADAR_DB` environment and DB are set up:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --status-refresh-once
```

Config defaults: `StatusRefreshMaxJobs=10`,
`StatusRefreshMinAgeHours=12`, `StatusRefreshRecentDays=14`.
Allowed limits: 1–30 jobs per invocation, 1–720 hours minimum status age,
1–90 days since RSS/discovery last seen. This feature is **opt-in/manual**;
not invoked by `--once` or the Kyiv scheduler.

## Strict source access

- Status checks only on the exact DOU domain `jobs.dou.ua` and
  Djinni domains `djinni.co` / `www.djinni.co`; matching approved
  vacancy paths, HTTPS port 443, no credentials/query/fragment.
- `HttpClientHandler.AllowAutoRedirect=false`: never follow a link
  to a different host, login page or job application endpoint.
  3xx responses are nonretryable errors, not closure evidence.
- HTTP 200 is NOT positive proof of an open vacancy. An Apply button
  without explicit validated status metadata is NOT positive proof.
- HTML status evidence is limited to the existing explicit
  provider-style banner, vacancy heading, page title and standalone
  closure message. `VacancyStatusEvidence` returns
  `IsOpen=false` for explicit closure or `null` otherwise.
- A 404/403/401/429, a timeout, parser exception or redirect
  is recorded as a retrieval failure, NOT `IsOpen=false`.
- This feature never fetches Robota.ua (blocked detail API), WWR,
  Work.ua or arbitrary URLs. It does not bypass website protections.

## Database invariants

- `ReadStatusRefreshCandidatesAsync` picks recent, sufficiently
  old unverified/stale-status DOU/Djinni jobs. Owner marked
  `Applied`/`Inactive` vacancies do not consume the limited scan budget.
- `SaveStatusOnlyAsync` updates only `open_status`,
  `status_checked_at` and `status_evidence` and appends one
  `job_status_checks` row, in a single database transaction.
  Older/out-of-order checks are ignored.
- **Never** alter `description`, `full_text_at`, `last_seen`,
  `title`, `published_at`, `job_discoveries`, or `job_queries`
  while checking status.
- An unknown latest HTML status supersedes previous provider status
  with `null` rather than asserting a closure is still current.
  Historical evidence remains in `job_status_checks`.
- The user-provided `employment_decisions` ledger is unchanged.
- `--status-refresh-once` reports selected, attempted, closed,
  unverified, failed and skipped, and exits nonzero on failed checks.

## Acceptance and status

Synthetic tests and PostgreSQL integration tests verify strict provider
allowlisting, no inferred `IsOpen=true`, stale-check ordering, append-only
history, unchanged job data and 3xx failure.

**No production crawl is claimed from offline tests.**
HP Trusted Git Relay v2 restore/build/test on the **exact PR head**
remains mandatory before merge.

**Mandatory handoff:** Follow
`docs/orchestration/AUTONOMOUS_RELAY_AND_HANDOFF_POLICY.md`.
If task publication is refused by a tool safety policy, do NOT circumvent
that policy through a different API or raw Git object. Obtain an
authorized infrastructure administrator handoff through the documented
`jobradar-v2-enqueue` helper; preserve every immutable task.
