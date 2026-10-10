# Phase 18 — Upwork integration first, then initial JobRadar operations

## Status and architecture

This workstream is intentionally independent of Draft WWR PR #24. Base main: ea77e23922e0f58bc5de3fd0c56c0100c9819050 (Phase 17 merged). The owner previously validated main at 232/232 tests; this new branch has **not yet been compiled or validated on Windows**.

**Upwork RSS was discontinued after August 20, 2024.** Do not add fake RSS endpoints or web-page HTML scraping. An **Upwork account is connected to ChatGPT's Upwork app** for direct authorized job search and details. That application connection does **not** hand reusable Upwork OAuth tokens to the local Windows JobRadar process.

Official references:
- Upwork [RSS deprecation](https://support.upwork.com/hc/en-us/articles/52052528243731-RSS-deprecation)
- Upwork [API authentication and security](https://support.upwork.com/hc/en-us/articles/115015933448-API-authentication-and-security)
- Upwork [developer documentation](https://www.upwork.com/developer/documentation/graphql/api/docs/index.html): own API key / OAuth2 required; caching limited to 24h under API terms
- Upwork [API and related legal terms](https://www.upwork.com/legal): do not remove source attribution, republish provider content or build unauthorised crawlers

We can search connected Upwork **in ChatGPT right now**, manually review potential contracts, and bring a short-lived **personal connector snapshot** into local JobRadar through a file. Local JobRadar **does not automatically authenticate to Upwork or poll it**. A full official API job collector is **blocked until separate Upwork application credentials, scopes and permission** are verified.

## Provider architecture decision: MCP and GraphQL are alternatives, not substitutes

Upwork's **official MCP server** is hosted at [https://mcp.upwork.com/mcp](https://www.upwork.com/ai/mcp) and supports OAuth 2.1/Dynamic Client Registration for compatible external AI clients. It already works in the user's connected ChatGPT account. **Those ChatGPT OAuth tokens are not available to JobRadar on Windows**; separate local OAuth registration/authorization and provider permission assessment are required.

Upwork also maintains the [GraphQL API](https://www.upwork.com/developer/documentation/graphql/api/docs/index.html), with separately managed API keys, OAuth2 and scopes. The standard API and MCP each have their own surfaces and access requirements; one is **not a drop-in replacement** for the other.

**Chosen rollout:**
1. **Working now:** connected Upwork MCP inside ChatGPT → bounded private JSONL snapshot → existing `UpworkProjects.TryAssess` and `OpportunityPreview` in JobRadar. A sample real snapshot contains eight Upwork jobs. No local OAuth credentials need to be copied into the repository.
2. **Future MCP adapter:** .NET MCP-compatible client using its **own user-approved OAuth 2.1 grant**, permitted search scopes and careful upstream error/limit handling. Not implemented in this PR; no claim of background Upwork polling.
3. **Future GraphQL adapter:** separate OAuth2-approved API client if API access/scopes allow the specified discovery workflow. Not implemented in this PR.
4. Both direct adapters should converge on the current normalized `UpworkSnapshot` / `UpworkCandidate` contract; no duplicate scoring, automatic bidding, or bulk storage of third-party descriptions.

An Upwork account connected within ChatGPT grants neither automated local access nor blanket permission to mirror job postings. The authorized read-only search capabilities of this account have been verified; future headless scheduling/retention still requires a provider-specific legal and technical go/no-go gate.

## Ephemeral snapshot format and CLI

Input is user-authorized private JSONL; **one flat JSON object per line**:

\`\`\`json
{"url":"https://www.upwork.com/jobs/~022108958663146489808","title":"C# .NET API integration","description_snippet":"ASP.NET Core integration","skills":["C#","ASP.NET Core","API Integration"],"job_type":"fixed","budget":"500.00","experience_level":"intermediate","published_date":"2026-10-10T16:31:47Z","captured_at":"2026-10-10T20:20:00Z","proposals_tier":"5 to 10","client_country":"United States","applied":false}
\`\`\`

The authorized connector's raw \`find_jobs\` response includes these fields except \`captured_at\` and a flattened \`client_country\`: the personal snapshot exporter adds the actual UTC retrieval time and copies the client COUNTRY only (not identity or message content).

\`--upwork-preview=FILE\` reads this file **without network or PostgreSQL** and prints *only* original title/link, provided budget text, score, risks and attribution, never descriptions or OAuth tokens. It uses the existing Freelancer-oriented C#/.NET/API/trading/AI scorer. Job types, senior/expert, Already Applied and English requirements are manual review signals, not permission to bid. **USD hourly ranges are kept as text, not falsely converted into numeric project budgets**. Avoid reapplying to already-applied jobs. The resulting scores are heuristics, not hiring probabilities.

The \`--opportunities-preview --upwork-file=FILE\` option includes ranked Upwork project rows with stored employment and optional other authorized snapshots. Import is **read only**; there are **no Upwork rows written to \`jobs\` or \`project_tracker\`**. Project applications/bids/Connects and provider notifications are never automated.

The importer:
- Rejects non-Upwork URLs, URL credentials, HTTP, custom ports and unexpected job paths; strips tracking query and fragment
- Requires \`captured_at\` less than **24h old** and no more than five minutes in the future
- Caps input to 2MiB, 100 lines, 32k characters per row, and bounded fields
- Deduplicates by canonical original Upwork job URL without merging unrelated titles
- Recalculates ranking locally; never trusts an imported rank
- Warns if the project was already applied to, marks all open status and eligibility **UNVERIFIED**
- Does not retain imported descriptions in DB or export them to the compact ranked stream

**Retention:** Keep the raw user-specific snapshot only in the local \`reports/\` folder (ignored by GitHub) and **delete it within 24h**. Do not commit it, synchronize it publicly or republish it. A pre-existing snapshot cannot prove provider-side liveness when later read. Verify new jobs/Connects via the official Upwork app before any bid.

## Day-one operations

The supported production-like entry point is \`scripts/operate-now.ps1\`. It deliberately starts no service, scheduled Windows task, Docker Compose collector or background job. You can run it **now**, including while continuing feature development, provided local PostgreSQL is reachable and \`JOBRADAR_DB\` is explicitly set to the owner's intended operational database.

### Phase A — Windows local acceptance first

\`\`\`powershell
Set-Location 'D:\projects\jobradar'
git fetch origin
git switch --detach origin/jobradar/phase18-upwork-local-operations

$secret=Read-Host 'postgres test password' -AsSecureString
$credential=[pscredential]::new('postgres',$secret)
try {
 $env:JOBRADAR_TEST_DB='Host=127.0.0.1;Port=54321;Database=jobradar_test;Username=postgres;Password='+$credential.GetNetworkCredential().Password
 powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-local.ps1 -RequirePostgres
 if($LASTEXITCODE -ne 0){throw 'Validation failed'}
}
finally {
 Remove-Item Env:JOBRADAR_TEST_DB -ErrorAction SilentlyContinue
 Remove-Variable credential,secret -ErrorAction SilentlyContinue
 git switch main
}
\`\`\`

Expected test count depends on this branch; don't claim PASS until the actual console result is supplied. GitHub Actions stays disabled.

### Phase B — operational read-only report (after acceptance)

Set \`JOBRADAR_DB\` **privately**, with the actual production DB name and password. \`JOBRADAR_TEST_DB\` does not qualify. Example shape (not actual credentials):

\`\`\`powershell
$env:JOBRADAR_DB='Host=127.0.0.1;Port=54321;Database=jobradar_live;Username=postgres;Password=YOUR_PRIVATE_PASSWORD'
.\scripts\operate-now.ps1 -UpworkFile '.\reports\upwork-connector-YYYYMMDD.jsonl'
\`\`\`

Default run **does not crawl**. It displays \`--pending-status\`, writes \`--triage-jsonl\` employer-vacancy reports to existing reports directory, and if supplied, processes the Upwork snapshot and emits a unified read-only opportunity preview. To **perform one actual new Ukrainian-source scan**, run:

\`\`\`powershell
.\scripts\operate-now.ps1 -RunCrawler -UpworkFile '.\reports\upwork-connector-YYYYMMDD.jsonl'
\`\`\`

Only after confirming no Docker/Windows scheduled collector is running concurrently. The \`--once\` crawler includes the current authorized core sources (DOU, Djinni, Robota); it **does not** query Upwork, WWR, Remotive, Freelancer or Jooble. Incomplete source status and parser failures may yield exit code 4 **while preserving findings**. The operator script continues report generation after partial crawl errors, then reports a non-successful operation instead of falsely claiming success; it stops immediately on DB-initialization/triage failures.

**Initial operations is NOT full autonomous 24/7 production deployment.** Next workstream: DB-wide scan lease, host/deployment health checks, authorized cadence and new-only notification delivery. Do not silently activate schedules.

## Roadmap after Upwork + first operations

1. Owner local validation of the Upwork branch, initial read-only and one-shot operational run.
2. Upwork: manual connected search and short-lived snapshots for early use; confirm API key access/scopes for optional direct local collector; never scrape HTML or spend Connects.
3. Classifier: put unrelated manager/security roles behind skill-fit checks, not in the top geo-check queue.
4. Broaden Ukraine DOU/Djinni/Robota query coverage; prove page/RSS caps; lawful additional sources only.
5. DB-wide scan lease and unattended operation on an actual host; standardized error and stale-source alerts.
6. Timely delta notifications and owner-authored application tracker for salary vacancies (freelance tracker already covers two other marketplaces).
7. User interface (ASP.NET Core) once everyday operation is stable.

**PR #24 WWR:** owner already validated it (266/266 and real 25-row live WWR RSS), but it remains Draft/unmerged. This Upwork branch is based on main and does **not** force-merge or replace #24.
