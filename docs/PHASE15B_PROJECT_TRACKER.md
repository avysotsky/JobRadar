# Phase 15B — Manual project tracker and application history

This stage adds a **local, manually maintained project tracker** for Freelancehunt and Freelancer.com. It does not capture provider API data, automate bidding, or alter the employment scheduler.

## Retention and source policy

- Supported sources: freelancehunt and freelancer. A provider URL is stored only after an explicit manual action; it is normalized to HTTPS, host and project path. Tracking/query fragments are discarded.
- The owner may enter a short personal label, a manually estimated budget in USD/EUR/UAH, and personal application notes. No imported project titles, descriptions, skill lists, client details, provider API tokens, provider response payloads, or automatic metadata enrichment are stored.
- Provider-derived content stays excluded from permanent storage pending explicit authorization and source-specific retention policies. An API token or access permission **does not automatically authorize indefinite retention**.
- All tracking data remains in the existing local PostgreSQL instance. The user can explicitly delete a tracked project and its own event history with the deletion command below. No automatic deletion is performed because these are user-created bookmarks and records of the user's own actions, not cached third-party content.
- All writes are fully local. No HTTP requests, no bids, no e-mails or applications are sent to providers. An Applied event records a past action performed by the user elsewhere; it never submits one.

## Database layout

Two additive tables created by Storage.Initialize, inside the existing serialized schema migration:

- project_tracker: provider, normalized URL, manually entered label/budget/currency, created_at, updated_at. One row per provider + canonical project URL.
- project_tracker_events: timestamped user-reported Applied, Replied, Interview, Won, Rejected, Withdrawn and Closed events; optional short own note. The current status is derived from the latest event, default Saved.

A partial unique index prevents more than one Applied event for the same tracked project. Recording Replied, Interview, Won, Rejected or Withdrawn requires a previous Applied event. A database transaction and row lock prevent concurrent duplicate recording. Deleting the parent project deletes all related own events via foreign-key cascade. No destructive migration or rewrite of historical job tables.

## PowerShell commands

Run these **after** setting JOBRADAR_DB to the intended local database. Do not point tests at the live database.

### Add a manual bookmark

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --project-track-add --project-provider=freelancehunt --project-url=https://freelancehunt.com/project/example/123.html --project-label="Manual .NET API project"
```

Optional `--project-budget=3000 --project-currency=UAH` are **your own entered estimates**, never automatically extracted from the provider.

### Record an action already completed on the website

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --project-track-event --project-provider=freelancehunt --project-url=https://freelancehunt.com/project/example/123.html --project-event=Applied
```

Use `--project-event=Replied` or `Interview`, `Won`, `Rejected`, `Withdrawn`, `Closed` for subsequent events. The optional `--project-note=` is an owner's short private note, limited to 500 characters. Do not include client secrets, contact data, personal identifiers or protected provider content.

### Inspect and delete

```powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --project-track-list
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --project-track-delete --project-provider=freelancehunt --project-url=https://freelancehunt.com/project/example/123.html --confirm-delete
```

List prints compact JSONL reference, owner-entered budget, status, timestamps and count of events, but **not note contents**. Deletion requires an explicit `--confirm-delete` and permanently removes all own events for that project.

## Unified opportunity preview

The Phase 15A `--opportunities-preview` now also includes manually tracked freelance references without requiring provider snapshots. Such items are conservatively `Review` with score 0 until authorized external metadata is supplied.

When an existing authorized JSONL provider snapshot matches a manually tracked canonical URL, the view **overlays the own application status** rather than doubling the project. `StatusEvidence=USER_RECORDED_APPLIED` (or the last action) is a user assertion, never verification that the project is currently open.

## Validation

This branch requires local Windows validation with .NET SDK 10 and PostgreSQL 16 isolated `jobradar_test` through `scripts/validate-local.ps1 -RequirePostgres`. Tests use random unique project URLs and delete them from the test DB. The test suite verifies URL/host guards, query-string stripping, budget and label validation, duplicate Applied prevention, event-order restrictions, cascade deletion, console privacy and overlay behavior.

**Never run these tests using jobradar_live.** No GitHub Actions workflows or provider API calls are required.

## Later work

Only after a provider-by-provider legal/permission review should an automated source-specific metadata cache be considered. Such a cache would need expiry, encryption/access safeguards, source attribution and deletion routines, without mixing temporary API snapshots with permanent user-authored application records.
