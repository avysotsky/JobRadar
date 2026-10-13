# Phase 31 — Remotive API in the main JobRadar employment crawl

## Scope

The official Remotive public remote-jobs API is added to the normal JobRadar
`--once` and regular Kyiv schedule, enabled by `EnabledRemotive=true`.

Documentation and access terms:
- https://remotive.com/remote-jobs/api
- https://github.com/remotive-com/remote-jobs-api

Remotive jobs are delayed about 24 hours. This source is **not a
real-time open-job verification**. Both the provider's original Remotive
URL and source name `remotive` are retained so the user follows the
listing on Remotive. API data must never be forwarded to other job boards
or used to collect registration/email data as a condition to view a job.

## Durable request limits

- An atomic PostgreSQL `remotive_daily_requests` table records attempted
  API requests **before** network activity.
- At most **two requests per UTC day** and at least **eight hours
  globally between requests**, including over midnight.
- A transaction advisory lock protects concurrent processes/date-boundary
  reservations. HTTP timeouts/failures **consume the reservation**.
- A quota skip produces a visible `SKIPPED_QUOTA` source status and makes
  **no HTTP call**. It is not a vacancy disappearance or a zero-vacancy
  observation, so it is excluded from historical count baselines.
- PostgreSQL outage fails closed before the API request. The quota applies
  to this automated core path; the historical manual `--remote-boards-once`
  command uses its own direct API mode, not this ledger. Avoid operating
  both paths concurrently until they share one ledger.

## Data handling

- One bounded (8 MiB maximum) HTTPS request to the official documented
  `remotive.com` software-dev API endpoint. Redirects are disabled.
- Parse the complete response in process; persist only .NET/C#/ASP.NET
  matches to prevent the bounded triage export from filling with
  unrelated stacks.
- Save `source=remotive`, original Remotive URL, publication date,
  employer, up to 2,400 characters of preview and the exact
  `candidate_required_location` field. No full descriptions are saved
  and no per-opening detail pages are requested.
- Data stays in the owner's private local PostgreSQL instance; API
  credentials are not needed. This does not constitute a public job board.
- Record `Remotive` in normal crawl coverage with `PARTIAL`, raw
  records and rejected URL counts; the initial response is not claimed
  to cover every .NET listing or every company.
- Country restrictions (e.g. `USA only`) are explicitly visible in
  the triage warnings. Only `Worldwide`, `Global`, `Anywhere`, and
  `Ukraine`-specific exact labels are considered positive country
  metadata; even those do **not** establish the job's current open
  status. All preview-only Remotive records remain in `NeedsReview`.

## Configuration

In `src/JobRadar/appsettings.json`:

```json
"EnabledRemotive": true
```

Turn off the new integration with `false` (the existing manual
`--remote-boards-once` remains independent).

With an already configured local `JOBRADAR_DB` connection:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --once
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --triage-jsonl
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --coverage-review
```

A normal run appends the Remotive source result to the same crawl report
as DOU, Djinni, Robota.ua and We Work Remotely. One or two daily runs may
be `SKIPPED_QUOTA` by design. Testing on HP uses only mock HTTP and
ephemeral PostgreSQL: it cannot verify live Remotive availability or
the owner's Windows runtime.

## Acceptance checklist

- Atomic reservation under concurrency and UTC day-boundary tests
- 2/day limit and 8h minimum, even if HTTP fails
- Strict external-link validation via existing Remotive parser
- No redirects or unbounded response payloads
- PostgreSQL persists preview, region and original provider URL
- Provider status remains unverified and requires human review
- Normal crawl report integrates Remotive source/status
- HP Trusted Git Relay v2 restore → build → test all PASS for the
  same exact source commit, cleanup 5/5
- No GitHub Actions enabled; no TradeOps Relay modifications
