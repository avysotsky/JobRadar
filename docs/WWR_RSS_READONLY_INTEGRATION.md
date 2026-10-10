# WWR production-readiness slice — public RSS, manual only

**Workstream:** WWR-A + WWR-C + in-memory WWR-B contract.  
**Base (2026-10-10 live GitHub):** `ea77e23922e0f58bc5de3fd0c56c0100c9819050`.  
**Baseline:** Phase 14 WWR/Remotive adapter, Phase 16 direct verified UTF-8 files, Phase 17 independent review priorities. No duplicate RSS parser or scheduling pipeline.

## 1. Sources and permission review

Official links checked 2026-10-10:

- [WWR public RSS](https://weworkremotely.com/remote-job-rss-feed): expressly offers public feeds for use by anyone with source-link attribution, including Backend, Programming, and Full-Stack.
- [WWR API Terms & Guidelines](https://weworkremotely.com/api-terms-and-guidelines): the API terms limit uses that compete with WWR (including job search services), require applying via WWR, and restrict scraping, copying and storing data. The explicit RSS usage statement **does not establish unlimited rights to persistent local database ingestion, full-page HTML scraping, or redistributing a job index**.
- [WWR Terms of Service](https://weworkremotely.com/terms-and-conditions), updated 2026-07-20.

**Permission conclusion:** Public read-only RSS access with attribution is permitted by the dedicated RSS page. The precise extent of provider permission for **ongoing DB retention/republishing** is unclear in light of the separate API conditions. JobRadar deliberately does **not** automate WWR database storage, HTML detail scraping, authentication, subscription features, application submission, or paid Pro access. Provider/source permission for a planned bulk-persistent job search index remains **BLOCKED/UNVERIFIED**, not PASS. Obtain written clarification from WWR before implementing `Storage.SaveDiscovered` for provider-derived records. Locally exported personal snapshots must not be redistributed; delete local historical snapshots when no longer needed.

All output retains the original WWR posting URL, and `Attribution="We Work Remotely"` with a live backlink. No session/cookie, login, CAPTCHA, HTTP redirect-following, Work.ua workaround or provider API integration is used.

## 2. Existing features preserved

Phase 14: `RemoteBoards.ParseWwr`, `Assess`, the two default WWR RSS feeds, deduplication and `--remote-boards-once` (2 WWR requests + 1 Remotive request). Phase 16: complete *received* RSS text for in-memory classification, 950-character compact excerpt, strict UTF-8 JSONL validation and atomic direct-file export. Phase 17: independent `ReviewPriority`, `ReviewEvidence`, no change to `FitBucket`, full audit including `Excluded`.

Source identity in historical JSONL remains `wwr-backend` or `wwr-programming`; do not silently rename it. Full text is **not** inferred from RSS content. Geo-eligibility and open status remain unverified.

## 3. Newly implemented

- `--wwr-once`: only the two WWR RSS feeds, no Remotive, no PostgreSQL. Output remains compact JSONL to stdout, diagnostics to stderr.
- `--wwr-once --wwr-output=reports/wwr-YYYYMMDD-HHmmss.jsonl`: reuses Phase 16 direct-file strict UTF-8/no BOM output, structural integrity verification and no-overwrite behavior. Recommended on Windows. `--remote-boards-audit=FILE` validates the resulting snapshot with no web or DB.
- `--wwr-fullstack` optional, only with `--wwr-once`: adds exactly one third WWR RSS feed, permitting comparison of new URLs and duplication. Neither full-stack nor the all-jobs RSS is fetched by default.
- Canonical URL identity: exact allowed HTTPS host and `/remote-jobs/<posting>` path, no credentials, no custom ports, no encoded path delimiters. Trim fragment, query tracking and final slash, normalize the `www` alias. Dedup by canonical provider posting URL, **not by title or company**. First feed `Source` is preserved for old JSONL compatibility; `CanonicalSource=wwr` and `FeedQueries` provide future canonical identity and all contributing RSS queries.
- Source metrics: each feed reports `RawRecords`, `ParsedRecords`, `DroppedRecords`, `AcceptedRecords`, `DuplicateRecords`, `InvalidDates`, `Status`, `Error` and `CoverageWarning`. Summary `DuplicateRecords` is the total cross-feed overlap. Empty feeds remain `PARTIAL` with an explicit warning, **not** evidence of zero available jobs. Failed feeds remain visible and other source results survive.
- An optional literal RSS `<company>` element is captured if supplied. Never extract employer identity from the title or invent salary/location/status. `PublishedAt` remains null for absent or malformed pubDate and `InvalidDates` increments. `ObservedAt` captures the local parse observation timestamp; its existence is not evidence a job remains open.
- XML DTD/external entities prohibited; RSS body bounded to **2 MiB**, Remotive API body bounded to **12 MiB**, strict UTF-8 decoding. HTTP 401/403/429/500 failures do not retry or downgrade. Combined and WWR-only scans retain **1.2 s spacing**, bounded by existing `TimeoutSeconds` (5–60 seconds) and a fixed finite endpoint list. No other-host redirects are allowed by Program's `AllowAutoRedirect=false`.
- `WwrEmploymentProjection.Project` produces an **in-memory** canonical `JobRef(Source="wwr")`, list of `FeedQueries`, attribution and observed time, explicitly `HasFullText=false` and `OpenStatus=null`. This bridges to the existing PostgreSQL employment schema without writing provider data.
- `EnabledWwr=false` is explicit in options and appsettings. It currently **does not activate ingestion even when switched on**: routine `--once` and Kyiv scheduled jobs never request WWR. `--wwr-ingest-once` is **blocked by design**, exits code 4 before DB initialization.

## 4. Usage — read-only, manually initiated

```powershell
Set-Location 'D:\projects\jobradar'
git switch --detach origin/jobradar/wwr-rss-readonly-hardening

# Offline validation: requires isolated test DB only for pre-existing integration tests
# JOBRADAR_TEST_DB must point to jobradar_test, NEVER jobradar_live.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-local.ps1 -RequirePostgres

# Optional live smoke: 2 WWR RSS requests, NOT Remotive, NOT PostgreSQL
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$path="reports\wwr-only-$stamp.jsonl"
dotnet run --project '.\src\JobRadar\JobRadar.csproj' -c Release -- --wwr-once "--wwr-output=$path"
$scanExit=$LASTEXITCODE

# Zero-network integrity validation
dotnet run --project '.\src\JobRadar\JobRadar.csproj' -c Release -- "--remote-boards-audit=$path"
$auditExit=$LASTEXITCODE
Write-Host "Scan=$scanExit Audit=$auditExit File=$path"

# Optional extra category: 3 WWR RSS requests, only when justified
# dotnet run --project '.\src\JobRadar\JobRadar.csproj' -c Release -- --wwr-once --wwr-fullstack
```

The existing `--remote-boards-once` still requests both WWR RSS feeds and one Remotive API request. **Do not run it purely to test this WWR workstream**: Remotive has independent API limits. For historical compact JSONL use `--remote-boards-review=PATH` with no external requests.

## 5. Coverage and acceptance evidence

**Historical real scan from owner (2026-10-10, before this branch):** WWR backend `4` records, WWR Programming `25`, overlap `4`, WWR unique `25`; combined with Remotive `18`, total unique `43`, `0 LikelyFit / 10 NeedsReview / 33 Excluded`, all configured sources `PARTIAL`. These **do not** verify new `--wwr-once` or current live category counts.

**Phase 17 baseline** was locally validated by owner: `232/232` xUnit tests, PostgreSQL 16 integration enabled, .NET SDK 10.0.401, zero warnings. This WWR branch introduces additional xUnit cases with fake `HttpMessageHandler` only: two-feed and three-feed overlap, query provenance, original link/attribution/observation, malformed XML/DTD, wrong-host/userinfo/port/encoded URLs, invalid date and missing metadata, country eligibility and language review, 403/429/500, redirects, body-size bounds, empty RSS, direct-file integrity and disabled DB schedule.

**New branch validation: NOT RUN YET** until the owner executes the Windows acceptance script. Do not infer new live counts or passing tests from prior phases.

## 6. Out of scope / blockers

- No new PostgreSQL writes, migrations, provider content retention policies or `job_discoveries` observations. Any future DB integration needs explicit WWR permission and deletion/expiry policy, source ID `wwr`, per-feed `job_queries`, open status null and `full_text_at` null.
- No full-page WWR scraping or verified job closure/opening/eligibility. `Anywhere in the World` can contain exclusions; Europe/EMEA/time zones do not imply Ukrainian residence eligibility. The existing eligibility/ranking rules determine warning/exclusion outcomes; a WWR RSS record can never be automatically `LikelyFit`.
- No schedule changes, subscriptions, API credentials, paid services, application automation, GitHub Actions or PR merge.

**Do not publish provider snapshot JSONL in the public GitHub repository.** Keep local sample fixtures synthetic.
