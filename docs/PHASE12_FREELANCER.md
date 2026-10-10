# Phase 12 — Freelancer.com project search (permission-gated)

## Purpose

Freelancer.com **projects** are freelance contracts, not salaried vacancy postings. JobRadar therefore searches them in an isolated manual command with project-centric ranking and does not put them into `jobs`, `job_queries`, `TriageExport` or `jobradar_live`.

This integration uses the official Freelancer.com API **only**. It does not scrape browser pages, bypass CAPTCHAs, use session cookies, or automatically bid.

### Permission requirement (critical)

Freelancer's User Agreement section 33 prohibits automated access (including API access) without **express written permission**. The official SDK also requires an OAuth2 access token. See:

- https://www.freelancer.com/about/terms
- https://www.freelancer.com/about/apiterms
- https://github.com/freelancer/freelancer-sdk-python
- https://developers.freelancer.com

**Do not run the scanner until Freelancer grants you the necessary written permission and a token for your authorized API application.** Having a Freelancer account, seeing an accessible public search page, or receiving an OAuth token does not itself establish written permission.

The command fails **before the first network call** unless both are present:

- `FLN_AUTOMATION_PERMISSION_GRANTED=true` — manual confirmation that written permission has genuinely been granted;
- `FLN_OAUTH_TOKEN` — an authorized OAuth token, passed in the documented `Freelancer-OAuth-V1` header.

The permission toggle is a safety gate, **not** a substitute for provider permission. Neither value is stored in the repository, logs or export. Never paste a token into chat or commit it to Git.

## Run (only after permission)

On the owner's Windows host:

```powershell
cd D:\projects\jobradar
$env:dotnet_root = 'd:\dotnet'
$env:path = 'd:\dotnet;' + $env:path

# ONLY after receiving written approval from Freelancer.com:
$env:FLN_AUTOMATION_PERMISSION_GRANTED = 'true'
$secret = Read-Host 'Freelancer OAuth token' -AsSecureString
$env:FLN_OAUTH_TOKEN = [pscredential]::new('token',$secret).GetNetworkCredential().Password
try {
    dotnet run --project src/jobradar/jobradar.csproj -c Release -- --freelancer-once
}
finally {
    Remove-Item env:FLN_AUTOMATION_PERMISSION_GRANTED,env:FLN_OAUTH_TOKEN -ErrorAction SilentlyContinue
    Remove-Variable secret -ErrorAction SilentlyContinue
}
```

**No database is necessary** to use `--freelancer-once`. This command is never called by normal `--once`, the scheduled crawler, `--triage-jsonl`, or `--retry-failed`.

The scanner emits one UTF-8 JSON object per unique project to **stdout**, ranked by relevance, with title, Freelancer project URL, preview text (capped to 450 characters), currency/budget (where known), skills, matched queries, score and `Priority`/`Review`/`LowMatch`. Progress and aggregate counts go to **stderr**. No permanent Freelancer project content is stored by JobRadar. Do not redirect outputs into lasting files unless permitted by the provider's data-retention, encryption and refresh rules.

## Search design

Config in `src/JobRadar/appsettings.json`:

- `FreelancerQueries`: `C# .NET`, `ASP.NET Core`, `trading bot`, `broker API`, `webhook automation`.
- `FreelancerPageSize`: default 20; clamped to 1–50.
- `FreelancerMaxPagesPerQuery`: default 1; clamped to 1–2.
- At most **8 unique queries**, **2 pages** each, **50 results** per page (16 API calls maximum). At least 1.2 seconds between calls.
- Search route: authenticated `GET https://www.freelancer.com/api/projects/0.1/projects/active/?query=<encoded>&limit=<N>&offset=<N>`, matching the endpoint and parameters used by the official SDK.
- Duplicate project IDs across queries are merged and query provenance is preserved; the record is not counted multiple times.
- `401`, `403`, `429` fail the command without retries. Unexpected response format never turns into a fictitious "no projects" result.
- The endpoint's `active` label is evidence at retrieval time only; project status and right to bid must be confirmed on the provider before submitting a proposal. No exhaustive-platform claim.
- Scores are **heuristic suitability indicators**, not predicted hiring success. Focus on C#/.NET, ASP.NET API/backend integrations, broker/exchange/trading automation, Python/ONNX. Review every proposal independently.

## Testing and validation

- Fully offline xUnit fixtures verify public API response interpretation, project fields, malformed SEO URLs, URL encoding, auth-before-network gate, no token leakage, deduplication, ranking and no retries on 401/403/429.
- No live Freelancer API calls were made during development; authorized credentials are not available to the assistant.
- Use the existing `scripts/validate-local.ps1 -RequirePostgres` with the **separate** `jobradar_test` database to validate the entire .NET project. GitHub Actions are intentionally disabled; never enable them without explicit instruction.
- This branch is independent of Phase 11 matching accuracy and should be merged without mixing employment/contract classification.

## Remaining work

1. Obtain written permission and OAuth application access from Freelancer.com.
2. Run one permissioned `--freelancer-once` and inspect responses/limits; adapt to any provider contract changes.
3. Evaluate project quality with manually labeled outcomes, not just headline keywords.
4. Investigate permitted encrypted, short-lived caching if the provider specifically authorizes it; no indefinite database retention by default.
