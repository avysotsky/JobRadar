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


## Local publication safety gate

The repository offers optional Git pre-commit and pre-push hooks in `.githooks/`.
The installer uses worktree-local Git configuration when multiple worktrees exist,
so a pinned operational worktree does not inherit the development hooks.
Review the source, then run `python scripts/test_publication_guard.py`
and `python scripts/install_publication_hooks.py` to enable hooks **locally**.
Installing hooks does not contact GitHub or enable any workflow.

- `python scripts/publication_guard.py --mode staged` checks staged blobs.
- `python scripts/publication_guard.py --mode branch --base origin/main` checks
  all introduced commits, including blobs subsequently deleted before HEAD.
- Seven synthetic unit tests cover normal files, literal application URLs, sample
  private email addresses, token-shaped strings, exports and commit history.

The scanner denies CV/export/database artifacts, credential-shaped data,
personal email/phone strings and literal owner-application CLI URLs. Results
print rule identifiers only, never private matches or filenames.

**These checks are heuristic and bypassable** (`--no-verify`, missing hook
installation, connector/API writes, PR descriptions/comments, and unknown
PII patterns). All public commits and PR metadata require independent manual
review. Hooks do not remove previously published sensitive Git objects.
