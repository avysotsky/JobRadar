# Phase 29 — Current-vacancy recommendation gates

## Motivation

The owner reported an inactive DOU Extracode Junior .NET opening appearing
in an October 13 recommendation, while an application to the Stepan
Junior+/Middle .NET position had already been sent.

A ranking score means **technical fit**, not that an employer is accepting
applications. The old shortlist mixed these concepts and treated unknown
source status as acceptable for `LikelyFit`.

## Changes

1. Status extraction recognizes explicit `вакансія неактивна`,
   `вакансія закрита`, Russian inactive equivalents and English inactive
   status in provider-style status banners, short standalone closed pages,
   or main vacancy title/heading. Ordinary descriptions mentioning other
   inactive job ads remain unverified.
2. An explicitly closed provider heading receives immutable status evidence
   `html:explicit-closed-heading`; the classifier excludes it only with
   verified `IsOpen=false` and this evidence. Existing banner and standalone
   closure evidence still work.
3. The recommendation export uses **a strict separate gate**:
   `LikelyFit` goes into the strong shortlist only if `IsOpen=true` has
   nonempty `StatusEvidence`, a `StatusCheckedAt` timestamp and a
   `LastSeen` within 24 hours. Otherwise a technically suitable job is
   `NeedsReview`, not falsely marked closed. This is the safe default,
   and it can initially produce an *empty* strong shortlist: current
   DOU/Djinni parsing does not assert positive open status just because
   the page returns HTTP 200.
4. Two manually entered employment decisions `Applied` / `Inactive`
   suppress that URL from recommendations but preserve the record in
   the complete audit. They are stored in the local PostgreSQL table
   `employment_decisions` and always labelled **USER_RECORDED**,
   not provider-verified. The exact canonical URL (ignoring trailing
   slash only, retaining meaningful query parameters) is the identity.
   Similar titles on distinct provider URLs are *not* blindly matched.
5. Unified `--opportunities-preview` uses these same owner/freshness
   gates for *stored employment*. Previously authorized WWR/Remotive
   remote-board JSONL snapshots remain an independent, read-only preview
   source and are always flagged as unverified opening/eligibility.
   Neither becomes scheduled nor copied to the core PostgreSQL jobs table.

## Owner commands (Windows, JOBRADAR_DB already configured)

Mark application already submitted to Stepan's vacancy:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- "--employment-mark-applied=https://robota.ua/company15404524/vacancy11112632"
```

Mark the reported inactive Extracode DOU vacancy:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- "--employment-mark-inactive=https://jobs.dou.ua/companies/extracode/vacancies/375238/"
```

Read local decisions and regenerate a new shortlist and review queue:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --employment-decisions
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --triage-jsonl
```

To combine the stored employment pipeline with a previously authorized,
validated international snapshot **without a new provider request**:

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --opportunities-preview "--remote-boards-file=reports/remote-boards-verified.jsonl"
```

A new WWR/Remotive scan must remain an explicitly requested separate
`--remote-boards-once` call, not an automatic side-effect of `--once`.
Remotive recommends a maximum of four API requests per day and requires
source attribution and direct backlinks. Work.ua remains disabled after
HTTP 403; Jooble still requires a separately supplied API key and bounded
request budget. No attempt is made to bypass those requirements.

## Interpretation

- `LikelyFit`: good technical fit plus explicit stored positive open
  evidence, current enough to recommend.
- `NeedsReview`: possibly suitable but status unverified or last seen
  more than 24 hours ago; the record is **not** claimed closed.
- `Excluded`: technical exclusion, provider-verified explicit closure,
  or owner-entered Applied/Inactive (with an explicit warning identifying
  the provenance).
- Manual reporting of `Inactive` never changes `jobs.open_status` or
  `status_evidence`. It is the owner's opinion/observation.
- A provider's RSS disappearance is not sufficient evidence of closure.
- Verification is intentionally strict. This phase does **not** create
  a new positive-status proof source or guarantee every live listing
  is still accepting applicants.

## Validation

Offline parser and gate tests, PostgreSQL upsert/isolation test and
triage integration test. HP Trusted Git Relay v2:
one exact SHA each for `restore`, `build`, `test`, with cleanup 5/5.
No live provider scans or GitHub Actions.
