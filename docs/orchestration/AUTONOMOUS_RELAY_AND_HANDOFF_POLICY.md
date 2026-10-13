# Mandatory autonomous development and handoff policy

**Owner instruction (2026-10-13):** Do not repeatedly involve the owner
or the HP administrator in ordinary JobRadar development, build, test,
diagnosis and retry loops. The assistant must carry out all actions that
the existing GitHub + Trusted Git Relay v2 tools permit. **Include this
policy explicitly in EVERY handoff file** authored for moving work to
another chat. Do not rely only on chat memory.

## Mandatory assistant workflow

1. GitHub is the source of truth. Inspect current `main`, the assignment,
   existing branches/PRs, and constraints before changing code.
2. Implement meaningful work in a separate branch and PR. Keep the
   existing owner-approved architecture, security controls, provider
   limits, and GitHub Actions-disabled policy.
3. **Independently** submit immutable `tasks-v2/<fresh-UUIDv4>.json`
   to `avysotsky/JobRadar-Relay-Queue` via the GitHub connector,
   targeting the **exact source commit**, following
   `docs/TRUSTED_GIT_RELAY_V2.md` in the Relay queue repository.
   Use the fixed supported project and actions, valid current UTC
   timestamps without milliseconds and a permitted expiry.
4. Verify each queued task by reading its stored bytes. Retrieve its
   corresponding `results-v2/<UUID>.json`; check exact UUID,
   action and SHA, `outcome`, `code`, `exit_code`,
   passed/failed/skipped and **all five cleanup flags**.
5. Run the full sequence **restore → build → test** with independent
   fresh UUIDs, one exact code SHA, and real PostgreSQL xUnit acceptance.
   Do not declare success from a successful GitHub commit, PR, mock
   results, or an incomplete runner output.
6. Diagnose failures **without delegating prematurely**:
   inspect repository code, relevant tests, Relay protocol, GitHub
   evidence and safe error metadata. Fix source errors on a new commit;
   then begin a new exact-SHA acceptance cycle.
7. If a runner returns an unexplained error but reports **verified
   cleanup 5/5**, make **one controlled, independent retry** with a
   *new* task UUID on the unchanged source SHA when safe. It must
   respect replay rules, expiry, infrastructure health and the
   applicable security policy. Never edit or reuse an immutable task.
   Do not spam retries or bypass trust/content/security checks.
8. Contact the administrator **only** when the necessary diagnosis or
   repair is genuinely inaccessible through existing tools, after the
   autonomous options are exhausted; examples are repeated infrastructure
   failures, missing host-only diagnostic evidence, or a blocked
   supervisor requiring authorized host access. If needed, send one
   precise ready-to-forward request including task UUID, source SHA,
   failure code and the minimal sanitized diagnostics requested.
   Do not ask the owner to run PowerShell, SSH or Docker as a routine
   substitute for using Relay.
9. After a clean PASS for all stages, review the exact PR/base/head,
   preserve branch freshness, merge only when safe, then verify that
   `main` has the tested code tree. State actual test counts,
   cleanup, commit SHA and merge status.

## Security and operational boundaries

- Never change the deployed Relay protocol/security rules, bypass
  `CONTENT_REVIEW_REQUIRED`, turn on GitHub Actions, or modify
  TradeOps Relay v1 merely to make a task pass.
- Source/content rejection calls for an honest code or test-fixture fix
  without weakening production security. Infra errors with adequate
  cleanup can justify one controlled retry; no automatic retry on
  security rejection or unclean resources.
- Relay acceptance is for **restore/build/test**, not proof of a
  live production JobRadar crawl. Do not quietly run external live
  provider scans or assume access to the owner's Windows/PostgreSQL.
- Prefer substantial autonomous work over frequent owner check-ins;
  brief progress updates are fine. Never claim the runner passed
  before a validated PASS result exists.

## Confirmed example: Phase 30, 2026-10-13

- PR #38: `https://github.com/avysotsky/JobRadar/pull/38`.
- Tested source SHA:
  `1a522bd2a746a6ba4e743abd80c8f10bd1dc591c`.
- Initial restore returned `RUNNER_ERROR`, exit 70, cleanup 5/5:
  `b17d9fc2-83e7-4a21-9e0c-2f4a337d6e1a`.
- Assistant independently resubmitted **once** with a fresh UUID:
  restore PASS `6a85d1c0-9f4a-4df7-bc73-e6a08e492185`.
- Autonomous build PASS:
  `2f63c428-e6d7-4b90-84ea-32d05cbf94f3`.
- Autonomous test PASS: **314 passed, 0 failed, 0 skipped**:
  `4ac93177-c24e-4820-a5a4-973bac6b45da`.
- All three successful stages had cleanup 5/5. PR merged,
  `main` SHA `ba9e2763360486650396636cbafdc180dcd3f220`,
  and `main` tree matched tested source tree. **No administrator needed.**

## Mandatory text for every future handoff

Include a distinct section titled **AUTONOMOUS DEVELOPMENT / RELAY
POLICY — MANDATORY**, preserving the substance of the numbered
workflow above and the following explicit sentence:

> The next assistant owns GitHub implementation, PR review, HP Relay
> restore → build → test, result validation, autonomous diagnosis,
> controlled safe retries, and merge verification. Do not ask the user
> or the HP administrator to perform steps that can be completed via
> existing tools. Escalate only after genuine tool-access or
> infrastructure blockers are established. Do not bypass security
> protections or claim unverified test results.

A handoff must also record: repository, main and working head SHAs,
branch/PR, exact tested SHA, task/result UUIDs, test outcome and counts,
cleanup flags, known limitations, unfinished work and next actions.
For other project repositories, restate the same **autonomy-first
principle**, adapted to that project's actual authorized CI/runner
mechanism; do not falsely claim that JobRadar Relay is installed there.
