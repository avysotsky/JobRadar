# Phase 7 — explainable Middle .NET vacancy triage

## Objective

Identify plausible remote C#/.NET backend vacancies for a Middle-level candidate, while retaining all unverified or excluded announcements in an auditable export. This is a **heuristic**, not a ground-truth vacancy or application-status detector.

## CLI

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -- --triage-jsonl
docker compose run --rm collector --triage-jsonl
```

The command reads the latest `ExportMaxRecords` vacancies from PostgreSQL (default 2,000) and produces timestamped UTF-8 JSONL files under `reports`: full audit, probable remote shortlist and manual-review queue. It sends **no HTTP requests** and never modifies or deletes job records.

## Screening model

- `WorkMode.Remote`: explicit indication of remote employment in the text. Even then, the location eligibility and latest open state must be checked separately.
- `WorkMode.Hybrid`: hybrid schedule or required office attendance. The strong shortlist explicitly excludes it.
- `WorkMode.Onsite`: office-only or prohibition of remote. Explicitly excluded.
- `WorkMode.Unknown`: remote mode not supported clearly by the text; sent to review, not recommended as confirmed remote.
- `FitBucket.LikelyFit`: explicit remote, full description, relevant C#/.NET match, enough relevant Middle/backend stack evidence, and no mandatory spoken/B2+ warning detected.
- `FitBucket.NeedsReview`: work mode unknown, description incomplete, English proficiency risk, mixed Middle/Senior titles, or weak stack fit. Saved in separate review JSONL.
- `FitBucket.Excluded`: explicitly hybrid or onsite, clearly Senior/Lead, stack unrelated to C#/.NET, or previously marked closed. Still included in full audit JSONL.

Scores reflect **rule matches**, not probabilities. Reasons list positive evidence; warnings list unresolved constraints (especially English B2, mandatory spoken calls, WPF, AWS/cloud). Regexes support common English, Ukrainian and Russian phrases but cannot reliably interpret every negation, requirement priority or employment policy. A job listing can change at any time.

## Quality and safety

- All output retains full source URL, description, employer, discovery queries, and the `IsOpen` tri-state (`null` remains unknown).
- B2/spoken English is flagged for manual review instead of being treated as an irreversible automatic rejection.
- A potentially closed or misleading listing must be checked against a provider before outreach.
- An explicit hybrid constraint must not be defeated by the presence of the word 'remote'.
- Neither score nor work-mode label is treated as an independently validated fact.

## Validation

Offline xUnit fixtures cover remote, hybrid days in office, onsite, misleading 'remote teams', irrelevant Java-only posts, Senior vs mixed Middle/Senior, B2 spoken English, closed vs unknown, and incomplete descriptions. PostgreSQL integration exercises full shortlist/review/audit exports and their provenance. Docker CI exercises the production CLI.

## Remaining

- Verify remote eligibility and job freshness from live provider metadata where publicly available.
- Improve cross-platform canonical deduplication and employer/source URL matching.
- Schedule delivery of new, high-confidence shortlisted vacancies through a secure opt-in channel after a real persistent collector is deployed.
