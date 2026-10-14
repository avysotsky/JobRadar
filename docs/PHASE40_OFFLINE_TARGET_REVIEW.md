# Phase 40 — offline target review from triage JSONL

## Context

The October 14 collector produced 549 JSONL rows in
reports/triage-all-20261014-101131-2431775.jsonl.
The Windows target-review command cannot connect to 127.0.0.1:5432.
The WSL2 containers jobradar-pr-autotest-postgres and
external-agent-postgres belong to separate automated systems: do NOT
repurpose, expose to Windows, or mutate them for operator review.

## Manual command (no PostgreSQL)

From the JobRadar repository on Windows:

    $file = 'D:\projects\jobradar\reports\triage-all-20261014-101131-2431775.jsonl'
    dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- "--target-review-file=$file"

The new flag is processed before Storage.Initialize. No database
connection, provider HTTP requests, report overwrites, crawling,
automatic employment decisions, or applications are performed.

Output is a JSON object containing the Phase 39 review (max 30 roles),
SourceRows, and explicit limitations:

- All suitability and review tiers are recomputed from the nested
  Vacancy, not trusted from historic Score/Bucket/WorkMode fields.
- The export DOES NOT contain the personal Applied/Inactive decisions
  normally read from PostgreSQL. Already-applied vacancies can reappear.
  Check them against personal application history before applying.
- Only vacancies in this historical export are analyzed; completeness
  of source-market coverage is UNKNOWN, not a fresh crawl.
- LastSeen is local collection time, not confirmed employer posting time.
  Opening evidence remains historical/unverified even if stored positive.
- Country/Ukraine eligibility must be verified at the original listing.

Input integrity: existing nonempty file; maximum 64 MiB, 50,000 rows,
2 Mi-characters per row; strict UTF-8; each nonblank JSONL line must
contain a complete nested Vacancy with HTTP(S) source URL and LastSeen;
duplicate source/URL pairs are rejected. Damaged or ambiguous files fail
closed with code 4 and no shortlist output.

Windows PowerShell 5.1 should use explicit UTF-8 to avoid mojibake:

    $first = Get-Content -Encoding UTF8 -TotalCount 1 $file | ConvertFrom-Json
    $first.Vacancy.Title
    $first.Vacancy.Description.Substring(0, [Math]::Min(180, $first.Vacancy.Description.Length))

## Acceptance

Draft PR requires established local WSL2 acceptance. Verify immutable
private avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/pr-<PR>/<HEAD>.json
against exact repository, PR, SHA, tested Git tree, restore/build/test
PASS, nonzero passed, zero failed/skipped, cleanup 6/6 true.
Only then mark Ready and merge with exact-head lease. No GitHub Actions,
legacy tasks-v2, empty retrigger commits or changes to host infrastructure.
