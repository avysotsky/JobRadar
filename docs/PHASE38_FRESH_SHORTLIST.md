# Phase 38 — Offline fresh vacancy shortlist

## Operator command

    dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --fresh-shortlist

Reads local PostgreSQL only: a bounded snapshot of jobs (ExportMaxRecords,
hard maximum 50,000), total number of stored jobs, explicit user
Applied/Inactive decisions, and earliest locally recorded discovery
timestamps in job_discoveries. It DOES NOT issue HTTP requests, alter
source or status data, change configuration, schedule crawls, or submit
job applications.

JSON result includes a ranked top 20 of target Junior+/Middle .NET Backend
jobs, linked directly to the original trusted board URL. Each row includes:

- Source, Url, Title, Company.
- FirstRecordedAt: earliest persisted job_discoveries observation. The
  audit window is the previous **7 days**.
- LastSeen and DiscoveryRecency: local feed ingestion recency, not proof
  the employer is currently hiring.
- PublishedAt: stored employer publication claim, not independently verified.
- StatusCheckedAt and StatusEvidenceLevel: POSITIVE_EVIDENCE_RECORDED_24H,
  POSITIVE_EVIDENCE_STALE, FALSE_STATUS_WITHOUT_VERIFIED_CLOSURE or
  OPEN_UNVERIFIED. These categories never assert present-day opening.
- Bucket, ReviewTier, Score, WorkMode, HasFullText, Reasons and Warnings.
  Scores are matching heuristics, not probabilities of hiring.

The shortlist prefers current gate-approved LikelyFit jobs, then top-tier
.NET Backend NeedsReview, then partial-.NET NeedsReview. Definite Senior,
office/hybrid, geographic restrictions, difficult B2+/mandatory relocation,
unrelated stack, and owner Applied/Inactive are not recommended. Unknown
remote eligibility or open status remains a human review task; an old
positive status cannot remain LikelyFit only on its historical timestamp.

Job URLs are verified as absolute HTTPS links on the known DOU, Djinni,
Robota or We Work Remotely host, with no credentials or fragments. This
is safe display filtering, NOT authorization to fetch restricted endpoints.

## Completeness and time boundaries

- A vacancy is 'new' here ONLY if its earliest AVAILABLE discovery event
  in job_discoveries falls within seven days; this history only began
  with Phase 15. Older records lacking event history are excluded, not
  assumed new. The original employer publication date can be older.
- The source data may be an incomplete RSS/API sample. No whole-market
  recall percentage is claimed.
- RecentRecordedJobs counts recently first-recorded jobs in the bounded
  snapshot, including ineligible jobs. EligibleForReview counts the
  retained target candidates before truncation to MaxItems=20.
- OmittedBySnapshotLimit > 0 means more stored rows were not examined,
  and command returns exit code 4 even if some candidates were printed.
  A complete valid local snapshot returns 0, including an empty shortlist.
  This does not imply there are no vacancies on public boards.
- A standalone operator run repeats the analysis against the current
  database, but this feature creates NO timer or recurring scheduler.
- Positive status recorded within 24 hours is evidence at that timestamp,
  not proof of availability at the moment of reading the report.
  Ukraine eligibility and English speaking requirements need manual review.

## Tests and acceptance

Pure xUnit tests cover selection order, unknown and stale positive status,
Senior/hybrid/EU-only exclusion, user Applied/Inactive, oldest discovery
boundary, missing history, trusted host allowlist, shortlisting limits,
older LastSeen and malformed counters. Isolated PostgreSQL tests verify
earliest-discovery MIN logic, rediscovery exclusion, owner action
suppression and bounded overflow. Tests do not call job boards.

Each new Draft PR HEAD is automatically restored, built and tested by
the local WSL2 watcher; immutable result path:

    PRIVATE avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/pr-N/FULL_SHA.json

Accept only matching PR, SHA and git tree, PASS for all three stages, a
nonzero test count with zero failures, and all six cleanup flags true.
Do not enqueue jobs in tasks-v2 or enable GitHub Actions.
