# Public repository privacy policy

This repository is public. Source commits, branch histories, pull requests,
PR descriptions, comments, reviews, test fixtures and documentation are
**public publications**. Do not store or publish the owner's real job search.

## Forbidden content in any GitHub artifact

- Employer, recruiter or contact identity associated with the owner's
  actual application, response, rejection, interview, offer, or compensation.
- Actual vacancy URLs or IDs **when tied to owner-specific actions**.
- Candidate CVs, personal contact details, transcripts, correspondence,
  private notes, salary negotiations or application history.
- Database dumps, operational JSONL snapshots, OAuth/API tokens, passwords,
  encrypted credential files and machine-local secrets.
- Copied real operational records presented as unit/integration fixtures.

## Required engineering practice

- Use fabricated synthetic companies, fictional applicant history and
  non-production fixtures in code, docs, PRs and tests.
- Real `Applied` / `Replied` / `Interview` / `Offer` events are owner-entered
  **only into a private local database** and never committed.
- Do not quote owner-specific CLI commands containing actual vacancy URLs.
- Verify staged changes, committed history and PR descriptions for confidential
  data **before** pushing or creating pull requests.
- Never rely on `.gitignore` alone: it does not protect GitHub PR bodies,
  comments, or manually staged tracked content.
- No external telemetry, GitHub Actions, scheduled scraping or auto-applying.
- If accidental publication occurs, immediately sanitize visible surfaces,
  rewrite published branch history only with appropriate safeguards, and
  request GitHub Support removal of cached/orphaned Git objects and PR refs.
  Force-pushing alone does **not** guarantee complete erasure.

See the user-controlled private PostgreSQL environment for operational status.
