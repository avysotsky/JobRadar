# JobRadar Phase 15A — Unified Opportunity Preview

Phase 15A adds a read-only combined view of employment and freelance opportunities, plus append-only discovery history for previously supported employment sources.

## Database and safety

The existing employment table remains the source of truth. A new job_discoveries table records source, URL, query and observation time for each SaveDiscovered call, in the same transaction as the existing job and discovery-query update. It does not rewrite or backfill prior observations. Earlier visits cannot be inferred: null history means unknown.

Stored crawl reports are reused for source-level coverage diagnostics. Reported query counts never establish board-wide completeness.

External board exports are optional operator-supplied local JSONL. The combined view does not make network requests and does not save third-party snapshots in PostgreSQL. Retention permissions and original attribution continue to be source-specific.

## Command

Use --opportunities-preview after configuring JOBRADAR_DB for the intended existing employment database. Optional file arguments are:
- --freelancehunt-file=PATH
- --freelancer-file=PATH
- --remote-boards-file=PATH

Run only with locally retained snapshots allowed by the provider. Do not provide credentials inside JSONL. Output is compact UTF-8 JSONL to stdout, diagnostics to stderr. No descriptions or OAuth tokens are emitted.

The output explicitly distinguishes Employment and FreelanceProject. Employment assessment buckets are LikelyFit, NeedsReview and Excluded. Freelance buckets are Priority, Review and LowMatch. Ranks from these two domains must not be compared directly.

Freelancehunt and Freelancer project rankings are recalculated from metadata. Remote-board compact snapshots may not contain the entire text originally used to classify the vacancy: imported Remotive LikelyFit is downgraded to NeedsReview rather than asserted confident. A snapshot cannot verify that a project is still active.

## Completeness and duplicate handling

Deduplication uses canonical URL within opportunity kind, with URL query parameters preserved. Never collapse different postings merely because titles match. Malformed imported lines and invalid provider-host URLs are counted as rejected. All external files are marked snapshot-only, not full-site coverage. Existing records predating the new event table have unknown individual discovery history.

Per-source imported counts, stored crawl results, and overall selected/omitted employment counts appear in the stderr report. The command exits nonzero for rejected input records or truncated employment output.

## Validation

Validate the entire solution using scripts/validate-local.ps1 -RequirePostgres with JOBRADAR_TEST_DB configured to isolated jobradar_test. This branch has not yet been run on the owner's Windows host.

Do not enable GitHub Actions. Do not merge before local testing.

## Future

Provider-authorized integration and retention policies are prerequisites for any later automatic storage of third-party project data. Persistent external metadata, purge rules, bid/applications tracking and real API smoke tests remain future work.
