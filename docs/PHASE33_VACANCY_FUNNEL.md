# Phase 33 — Offline per-source vacancy actionability funnel

## Purpose

`--coverage-review` measures observed crawl/source anomalies but does not
explain why a database with many records yields few vacancies worth manually
checking or applying to. The new `--vacancy-funnel` command aggregates the
**stored vacancies**, using the exact existing triage and recommendation gates.
It produces counts by stored canonical `source`, not by RSS query (see
`--rss-query-recall` for per-query discovery attribution).

## Command

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --vacancy-funnel
```

Requires the existing `JOBRADAR_DB` connection. Runs `Storage.Initialize`
at normal process startup, then **reads only** the existing vacancies and
user decisions for this report. It makes **no provider HTTP calls**, performs
no crawler, status refresh or application action, and changes no saved job
status, timestamps, histories, configuration, scheduling, quota or decision.

The report honors `ExportMaxRecords` (clamped to 1–50,000). It includes
`Evaluated`, `TotalStored`, `OmittedByLimit`, aggregated `Totals`, and
`Sources` grouped by canonical stored `source`, ordered alphabetically.
If there are no selected vacancies or the export is truncated
(`OmittedByLimit > 0`), CLI returns exit code 4 **while still displaying**
the partial JSON report; otherwise exit 0. A nonzero code does not prove
anything about provider uptime. For a full stored-database audit, increase
the local `ExportMaxRecords` within its supported limit and rerun. A
concurrent ingestion can change total rows between the read and count;
this report is not a transactional snapshot of a live crawler.

## Meaning of the counts

- `Stored`, `FullText`, `PreviewOnly`: number of rows and whether full
  description is cached; preview-only is **not** proof of a poor match.
- `SeenWithin24Hours`: a recent **discovery** timestamp, NOT proof that
  employer applications are still accepted.
- `PositiveStatusEvidenceRecorded`: an already stored positive boolean,
  status timestamp and nonempty evidence; may be historical. This does not
  promise current eligibility, and status-only Phase 32 never manufactures
  positive opening from HTTP 200 or an Apply button.
- `ExplicitProviderClosed`: a false status accompanied by one of the
  existing explicit HTML closure evidence codes.
- `StatusUnverified`: neither of the above. Includes absent/legacy status
  evidence and is **not** counted as provider-closed.
- `RemoteTextEvidence`: the existing `WorkMode.Remote` classification,
  not legal permission to work remotely from Ukraine.
- `GeographicRestriction`: explicit restriction detected by the existing
  eligibility analyzer. An *absent* restriction is **not** proof of eligibility.
- `EnglishB2PlusMentioned`: warning signal for B2/C1, not proof that
  spoken English at that level is mandatory.
- `OwnerApplied`, `OwnerInactive`: user records independent from
  provider observations. Both remain counted in `Stored` and are excluded
  from recommendations, **not** transformed into provider closures.
- `LikelyFit`, `NeedsReview`, `Excluded`: the existing
  `VacancyTriage.Assess` plus `VacancyRecommendationGates.Apply`
  classifications. A high matching score alone cannot establish actionable
  `LikelyFit`; missing positive status produces review.
- `ReviewTargetBackend`, `ReviewPartialDotNet`,
  `ReviewDifficultRequirements`, `ReviewOtherSpecialization`: disjoint
  subdivisions of `NeedsReview` under Phase 31's ranking rules. Their sum
  equals `NeedsReview` for each group.

This diagnostic does **not** estimate whole-market completeness, count
today's open postings from an RSS feed, or replace direct examination of a
specific employer's offer. It does not output private application history
or individual vacancy URLs.

## Acceptance

- Unit tests for positive/unknown/explicitly closed statuses, stale
  discovery, EU-only restriction, advanced English, source partitioning,
  preview-only rows, truncated snapshots, and user Applied/Inactive evidence.
- PostgreSQL-backed test against actual saved jobs and decisions.
- Automatic WSL2 **Draft PR HEAD** acceptance:
  `restore → build → test`; immutable result in private
  `avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/pr-<N>/<SHA>.json`.
  Require exact PR/SHA, all three PASS, nonzero tests with zero failures,
  six cleanup flags true. No `tasks-v2/` creation.
- GitHub Actions remain disabled; Work.ua/Robota 403 controls,
  Jooble/Remotive request limits and all existing safety gates are unchanged.
