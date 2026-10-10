# Phase 16 — International remote-feed integrity and classification hardening

## Evidence: owner's first live scan, 2026-10-10

Actual combined scan returned 47 raw references and 43 unique listings: WWR backend 4, WWR programming 25, Remotive 18, with four duplicate WWR references. Provider calls all succeeded and no records were dropped by the **provider parsers**. This did **not** prove the saved JSONL was valid.

Independent byte-level inspection of the exact owner-supplied `remote-boards-20261010-172035.jsonl` found 43 physical lines. Invalid UTF-8 occurs on **23, 30, 41**; invalid JSON on **27, 31, 41**. Thus only **40/43** lines parse as JSON after lossily replacing invalid UTF-8. Cyrillic diagnostic strings were also damaged. These are symptoms consistent with legacy Windows native-process output encoding and best-fit substitutions; an isolated definitive root cause was not established.

The damaged historical file cannot be repaired losslessly. It is preserved as historical evidence. No provider data or proprietary posting descriptions from it are committed to GitHub.

## Implemented

1. Explicit UTF-8 for .NET console stdout and stderr, independent of default Windows OEM encodings.
2. Optional `--remote-boards-output=PATH.jsonl` for `--remote-boards-once`. .NET writes the output **directly** to a unique UTF-8-no-BOM file, not through PowerShell redirection. A temporary file is first closed, strictly checked for UTF-8 + JSON syntax + expected structure and row count, and only then moved into place. Existing reports cannot be overwritten. Incomplete temporary reports are removed on failure.
3. Offline `--remote-boards-audit=PATH.jsonl` reports `Lines`, `InvalidUtf8`, `InvalidJson`, `InvalidShape`, and **exact one-based line numbers** for each. The two error classes are independent: line 41 may have both. Exit code 4 means an invalid file. It does not call any job provider or PostgreSQL.
4. Strict validation of `--remote-boards-file=...` passed to `--opportunities-preview`; corrupted remote snapshots now fail explicitly. All local JSONL imports use a strict UTF-8 decoder.
5. WWR classification uses **the entire RSS description received** before output truncation; exported excerpts remain limited to 950 characters and `HasFullText=false`. The complete RSS text is transient, not serialized or stored. WWR still remains `NeedsReview` unless independent full-text and work-from-Ukraine evidence are verified.
6. Regression tests reproduce the *failure pattern* of the historical 43-line export without storing real third-party listing content. Other tests verify late C#/.NET and English B2 requirements after character 950, safe direct-file roundtripping of Cyrillic/typographical quotation marks/bullets and no overwrite.

## Offline audit on Windows (no provider API requests)

Use a checked out Phase 16 branch. In PowerShell:

```powershell
cd D:\projects\jobradar
dotnet run --project .\src\JobRadar\JobRadar.csproj -c Release -- --remote-boards-audit="reports\remote-boards-20261010-172035.jsonl"
```

Expected legacy findings: 43 lines, 3 UTF-8 failures (23, 30, 41), 3 JSON failures (27, 31, 41), and exit code 4. The original file remains unchanged.

## New live scan (only when you choose to spend provider requests)

```powershell
cd D:\projects\jobradar
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$path="reports\remote-boards-verified-$stamp.jsonl"
dotnet run --project .\src\JobRadar\JobRadar.csproj -c Release -- --remote-boards-once "--remote-boards-output=$path"
if ($LASTEXITCODE -ne 0) { Write-Warning "Some source calls failed or yielded dropped records. Check stderr report." }
dotnet run --project .\src\JobRadar\JobRadar.csproj -c Release -- "--remote-boards-audit=$path"
```

Unlike Windows `Start-Process -RedirectStandardOutput`, the native direct-file mode does not depend on console output encoding. A nonzero scan exit code may mean partial coverage even if the published file is structurally valid. This command does not schedule scans; use provider terms and Remotive rate limits.

The source-level `CoverageStatus=PARTIAL` remains correct: receiving all requested provider endpoints does not prove site-wide enumeration, active-job status, or working-from-Ukraine eligibility.

## Local acceptance gate

Run `scripts/validate-local.ps1 -RequirePostgres` against isolated `jobradar_test`, not `jobradar_live`. The baseline on main was **200/200** tests (owner validated). Phase 16 adds four tests; expect **204/204** after local validation, with zero warnings.

GitHub Actions **remain OFF**; no new workflows or remote CI.

## Not claimed / follow-ups

- We have not proven the exact Windows process-code-page path that corrupted the original file, only established a reliable output route that bypasses it.
- No source can prove eligibility to work from Ukraine from a generic `Remote` label alone.
- Full WWR posting retrieval, explicit location-policy verification, and further role-specific ranking of adjacent AI/manager/security jobs remain separate development work. Do not turn incomplete RSS into a definitive exclusion based merely on absence of .NET in a preview.
