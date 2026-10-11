# Phase 20 — Windows Task Scheduler with protected PostgreSQL credentials (OPT-IN)

## Status and dependency

- **Phase 18:** first real Windows `jobradar_live` run: 328 references across
  7 search queries, 290 distinct employment records, 13 LikelyFit; Upwork
  ephemeral snapshot 8 projects. All seven query coverages were PARTIAL.
  Full Robota text remains blocked by HTTP 403.
- **Phase 19:** same-database PostgreSQL advisory lease for `--once`, built-in
  schedule, `--retry-failed` and opt-in Jooble. Owner Windows
  **257/257 xUnit PASS**, PostgreSQL integration enabled, exact accepted HEAD
  `58cefc7e78334d4603b35fe479ec2c599fdb4222`.
- **Phase 20:** scripts for *proposed* Windows scheduled operations, stacked on
  Phase 19. **No schedule was registered on the user's machine, no credentials
  created and no unattended live scan performed by GitHub.** Scripts must be
  locally syntax-checked and owner-approved before enabling.

## Properties

1. Credentials are a Windows DPAPI-encrypted PowerShell `PSCredential` in
   `%LOCALAPPDATA%\JobRadar\postgres-credential.xml`. That file must NEVER
   enter GitHub, reports or a chat. It decrypts only under the same Windows
   user profile. No plaintext password in scheduled-task arguments or files.
2. Each task invocation verifies the exact `git rev-parse HEAD` SHA and
   refuses tracked modifications before invoking `dotnet run -- --once`.
   Changing developer checkouts cannot silently change the code run by the
   schedule. Use a **dedicated stable Git worktree** for operation.
3. Three triggers: **08:00 / 13:00 / 19:00 machine-local time**. The
   current Windows timezone must therefore be Europe/Kyiv; otherwise those
   hours do **NOT** mean Kyiv time. Check `Get-TimeZone`. Never change the
   user's timezone without their explicit approval.
4. Tasks use `Interactive` identity for the **current logged-on user** so
   DPAPI works; they do not run after logoff or while the PC is off. A
   missed trigger may run on the next suitable logon when Task Scheduler's
   `StartWhenAvailable` applies. Task settings ignore overlapping instances,
   and Phase 19's PostgreSQL lease rejects simultaneous cooperating scans.
5. Raw scheduled crawl logs and triage summaries live under
   `%LOCALAPPDATA%\JobRadar\logs`; the script prunes its `*.log` files
   older than 30 days. Existing full triage JSONL reports remain in the
   repository's git-ignored `reports/`. On exit **4 = DEGRADED**, reports
   remain available; **5 = BUSY** means no new scan. Exit **3 = DB unavailable**
   and **6 = triage-export failure**.
6. **No scheduled Upwork**. The official Upwork MCP currently runs only
   through the user's authorized ChatGPT connection; 24-hour private JSONL
   snapshots cannot be recycled into this unattended collector. No
   auto proposals, Connects spending, automatic Freelancehunt/Jooble scans,
   Github Actions or hidden browser/scraping.

## Validation before enabling — no registration, no network

From an exact Phase 20 checkout on the owner's Windows computer:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\phase20-validate-windows.ps1
```

This parses all three scripts with PowerShell's own parser. Also run the
existing `scripts\validate-local.ps1 -RequirePostgres` using the isolated
`jobradar_test`; expected **257/257 .NET xUnit** because Phase 20 does
not alter source or test projects. Verify that all exit codes are zero.

Inspect the registration plan without scheduling:

```powershell
$approved = (git rev-parse HEAD).Trim()
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-windows-schedule.ps1 -ExpectedHead $approved
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-windows-scheduled.ps1 -ExpectedHead $approved -DryRun
```

Both commands must print preview/dry-run, **not create credentials or contact
PostgreSQL**.

## Owner-only installation after verification and approval

**Stable checkout:** Recommended to keep developer work separate. From the
original `D:\projects\jobradar` repository, with explicit chosen and
reviewed `$approved` commit:

```powershell
$approved = '<EXACT REVIEWED PHASE20 HEAD SHA>'
git worktree add --detach "D:\projects\JobRadar-ops" $approved
Set-Location "D:\projects\JobRadar-ops"
```

Before creating any credential or registering, check `Get-TimeZone`,
the actual Windows user identity, connection to **`jobradar_live`**, and
that another legacy Docker or `dotnet --loop` collector is not active.
Do NOT use `jobradar_test` or overwrite existing scheduled tasks.

Create user-scoped encrypted operational credential interactively (not in
Task Scheduler and not through a remote execution):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\set-windows-credential.ps1
```

This asks for the PostgreSQL password (hidden input). There is **no
database operation** at this step. To rotate an existing encrypted credential,
explicitly use `-ReplaceExisting`; old credentials are never overwritten
silently.

**Register only when the owner chooses to enable unattended scans:**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-windows-schedule.ps1 -ExpectedHead $approved -Register
Get-ScheduledTask -TaskName 'JobRadar-Collector-Local' | Get-ScheduledTaskInfo
```

This creates a single current-user Task Scheduler job with 3 daily triggers.
It does not run the collector immediately. To remove it:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-windows-schedule.ps1 -ExpectedHead $approved -Unregister
```

Unregistration deliberately leaves the DPAPI credential in place. The owner
can securely remove the file independently after ensuring it is no longer
used. `git switch` in the development checkout will not change the
dedicated `JobRadar-ops` worktree.

## Known operational limitations / follow-up

- Task Scheduler requires a logged-on interactive user; this is deliberately
  narrower than a real Windows service or unattended 24/7 server.
- A running PostgreSQL lease is released if the dedicated DB session is lost;
  a future health-watchdog/heartbeat can detect prolonged session loss.
- No claim of exhaustive provider coverage, all vacancy openings verified,
  or restored Robota full text; source status may remain DEGRADED indefinitely
  until explicitly resolved.
- This PR is a **stacked DRAFT** on accepted Phase 19. It must not be merged
  into main before its parent branches are merged by owner authorization.
- Installation is a separate explicit user action. GitHub Actions are OFF.
