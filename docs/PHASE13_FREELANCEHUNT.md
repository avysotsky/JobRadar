# Phase 13 — Freelancehunt API v2 project discovery

## Status

Read-only **manual** collector for freelance projects, separate from employment vacancies in PostgreSQL. No API access key or real provider response is included in the repository. Unit tests use synthetic API v2 fixtures.

Official provider contract: [Freelancehunt API 2.0](https://apidocs.freelancehunt.com/). The `GET /v2/projects` endpoint returns open projects by default with `data` records and `links.next` pagination. Optional server-side skill filtering uses `filter[skill_id]` with **numeric skill IDs**, NOT keyword text; page index is `page[number]`. Auth: `Authorization: Bearer <token>`; request language `uk`.

A reference JSON resource from the provider ecosystem is used for field layout: `data[].id`, `data[].attributes.name`, `description`, `skills`, `budget.amount`, `budget.currency`, `bid_count`, `is_only_for_plus`, `is_remote_job`, and `links.self.web`. Treat provider changes as explicit schema errors or dropped/partial records.

## How to run

Only when you have your own provider-authorized API key (Freelancehunt account **Apps and API**):

```powershell
cd D:\projects\jobradar
$env:DOTNET_ROOT = 'D:\dotnet'
$env:PATH = 'D:\dotnet;' + $env:PATH

$secret = Read-Host 'Freelancehunt API key' -AsSecureString
$env:FREELANCEHUNT_API_TOKEN = [pscredential]::new('api',$secret).GetNetworkCredential().Password
try {
    dotnet run --project src/jobradar/jobradar.csproj -c Release -- --freelancehunt-once
}
finally {
    Remove-Item Env:FREELANCEHUNT_API_TOKEN -ErrorAction SilentlyContinue
    Remove-Variable secret -ErrorAction SilentlyContinue
}
```

Do **not** paste an API key into ChatGPT or commit it. The command fails before network access when the key is absent; redirection is disabled to prevent credential forwarding.

It prints UTF-8 JSONL to standard output and short progress/summary to stderr. **No PostgreSQL connection, writes, application bids, or hidden browser scraping.** This command is never scheduled by the ordinary `--once` vacancy collector.

## Configuration

```json
"FreelancehuntSkillIds": [],
"FreelancehuntMaxPages": 2
```

- Empty skill IDs means open projects are fetched without a server-side skill filter, then scored client-side. With a page cap, it may miss most C#/.NET jobs. **Avoid claiming exhaustive coverage.**
- Supply only **provider-issued skill IDs** (not arbitrary guesses), maximum 12 unique positive IDs. The API accepts comma-separated IDs.
- Fetch 1–3 pages maximum; 1.2 seconds between requests. Respect provider rate-limit responses. 401, 403 and 429 stop without retries. Pagination includes raw record counts, dropped malformed records and an `IncompleteEnumeration` boolean.
- Project URLs are accepted only from provider-supplied `https://freelancehunt.com/project/...` links. The scanner does not follow page redirects or invent project slugs.
- Repeated project IDs are deduplicated. Provider flag `is_remote_job=false` is **not** proof of office attendance: most ordinary freelancing projects are not categorized as remote salaried jobs.
- A project requiring Plus access is downgraded from Priority to Review. Availability, bidding cost/eligibility and relevance must still be checked on the provider website.

## Isolation and ranking

This is **project-oriented ranking** reusing Phase 12's common project score: C#/.NET; REST/Web API/backends; trading/brokers/crypto; automation/AI/Python. Results use `Priority / Review / LowMatch` rather than `LikelyFit / NeedsReview / Excluded`.

Data retained only for the duration of the command, including a 1400-character description excerpt. Exporting to a file is optional via operator console redirection and should comply with provider terms. No job/credential data is stored in PostgreSQL.

## Validation / known limits

- The branch must pass local `scripts/validate-local.ps1 -RequirePostgres` using *only* `jobradar_test` as the test database, though Freelancehunt tests themselves are offline and independent from PostgreSQL.
- The API field shape is grounded in published documentation/sample data, **not a live authenticated call on the owner's account**. Current provider API responses and limits remain unverified until an owner-authorized smoke test.
- Scans are deliberately bounded and marked PARTIAL regardless of whether the configured pages were fully collected. The API does not offer free-form keyword filtering for projects in the published list endpoint; skill ID filters may improve relevance.
- Not supported yet: bidding, automated applications, persistent project tracking/alerts, skill ID lookup, cross-provider freelance deduplication.
- GitHub Actions stay disabled, and no CI workflows are added.
