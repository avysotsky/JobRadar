# AUTONOMOUS DEVELOPMENT / RELAY POLICY — MANDATORY

**Owner instruction (2026-10-13):** Develop and validate JobRadar autonomously without
requiring the owner or an HP/OpenClaw administrator to transfer routine build or test tasks.

**Protocol effective 2026-10-13: automatic WSL2 Draft-PR watcher.**
This newer mechanism **supersedes** the old JobRadar `tasks-v2/` and
`results-v2/` queue workflow for **all new JobRadar test cycles**.
Those older files are retained as historical evidence; do not publish new ones.
The change applies **only** to JobRadar. TradeOps and DocFlow retain their
own separately authorized relay queues and protocols.

## Mandatory assistant workflow

1. GitHub `avysotsky/JobRadar` is source of truth. Before each action,
   inspect current `main`, relevant Draft PR, source files, head/base SHA,
   open review comments and existing autotest result for the exact head.
2. Implement a cohesive change on a dedicated branch with a **Draft PR**.
   A new commit to a Draft PR produces a new full 40-character HEAD SHA.
3. The installed local **WSL2 watcher** detects each newly observed PR HEAD
   (`refs/pull/<PR>/head`) automatically and runs sequential
   `restore → build → test` for
   `tests/JobRadar.Tests/JobRadar.Tests.csproj`. The assistant does
   **not** create task files, call enqueue helpers, or ask an administrator
   to dispatch ordinary jobs.
4. Read the immutable result from PRIVATE
   `avysotsky/JobRadar-Relay-Queue`:
   `autotest-results/jobradar/pr-<PR_NUMBER>/<FULL_SHA>.json`.
   Never accept a result for another SHA or PR. Historical
   `.acceptance-N.json` files are bootstrap evidence, not a current-head
   substitute.
5. Verify `version=1`, `repository=avysotsky/JobRadar`,
   matching `pr` and `sha`, `overall=PASS`, stage statuses
   `restore=PASS`, `build=PASS`, `test=PASS`,
   test counts (nonzero passed, zero failed, explicitly inspect skipped),
   and all **six** cleanup flags `true`: `database`, `role`,
   `runner`, `secret`, `snapshot`, `workspace`.
   An absent result is *pending/unverified*, never PASS.
6. On `FAIL`, inspect bounded nonsecret status, source and tests;
   correct the actual issue with a **new commit**. The watcher then
   evaluates that **new SHA** once. Do not try to requeue/rewrite old-SHA
   results. Infrastructure and trust-policy failures are not permission
   to bypass a safety control.
7. After exact-current-head PASS, check PR mergeability and latest base,
   ensure changes integrate safely with the current `main`, merge
   sequentially and verify that `main` contains the exact tested code
   (or test the actual integrated tree before claiming acceptance if the
   merge materially alters that tree).
8. Keep the owner informed of material progress and blockers but do not
   repeatedly ask for PowerShell, SSH, Docker, database operations,
   log transfer or manual PR handling when tools can do it.
9. Preserve GitHub Actions disabled, provider quotas, HTTP 403 access
   restrictions, privacy rules, user `Applied/Inactive` decisions,
   provider-status evidence gates and budget boundaries. Never run
   live vacancy crawls as part of offline acceptance.

## Security and operational boundaries

- Source of truth is the exact GitHub PR HEAD plus corresponding immutable
  watcher result. A comment, documentation-only update or old PASS does
  not prove a different SHA passed.
- Do not publish new JobRadar tasks in `tasks-v2/` or use
  `results-v2/` for new acceptance. Do not bypass refusals via Git
  blob/tree/ref APIs, encoded payloads, altered runner policies or
  GitHub Actions.
- WSL2 `restore/build/test` with isolated PostgreSQL does **not**
  establish live provider crawl success, current vacancies, Ukraine remote
  eligibility or access to the owner's production PostgreSQL.
- A new SHA is required after changing any source or test code.
  Do not fabricate runner metrics, status or cleanup flags.
- If watcher output is persistently unavailable on newly observed HEADs,
  record exact PR/SHA and missing path, inspect GitHub evidence and
  escalate *once* only if a genuine host-level issue remains.

## Handoff requirement

Every future handoff must include this heading and preserve this explicit
instruction:

> The next assistant owns GitHub implementation, PR review, autonomous
> WSL2 Draft-PR restore → build → test result validation, autonomous
> diagnosis, safe new-commit retries, and merge verification. Do not
> ask the user or administrator to perform steps that the existing
> GitHub and watcher tools already handle. Do not bypass security
> protections or claim unverified test results.

Record at minimum repo, latest `main`, each PR branch/head/base,
exact tested SHA, watcher result path, restore/build/test outcome,
test counts, all six cleanup flags, actual merge status, blockers
and next actions. For other projects, state their *actual* protocol;
never assume JobRadar's WSL2 watcher covers TradeOps or DocFlow.

## Historical reference only: previous HP Trusted Git Relay v2

Prior to the WSL2 watcher, JobRadar used immutable
`tasks-v2/<UUID>.json` and `results-v2/<UUID>.json` via the
private Relay Queue. The successful Phase 30 cycle used exact source
SHA `1a522bd2a746a6ba4e743abd80c8f10bd1dc591c` with
314 passed, zero failed and five-resource cleanup. This history
is not an instruction to return to task publication or a current
PR acceptance result.

The watcher deployment bootstrap result for PR #41 SHA
`71dec095f4eefb5a236f68081e33c05e07b41f5e` recorded
337 passed in an `.acceptance-4.json` result. It does
**not** qualify any subsequent PR #41 HEAD.
