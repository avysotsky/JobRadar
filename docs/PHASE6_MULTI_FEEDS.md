# Phase 6 — multiple independent RSS discovery queries and persistent deployment

## Why

Prior DOU and Djinni adapters consumed one capped RSS view each, potentially losing relevant posts while the source continued publishing. Additional public keyword feeds can surface different posts, and the canonical job index deduplicates overlaps.

## Implemented

- DOU: default .NET/remote feed plus extra C# / remote RSS query.
- Djinni: default .NET/remote RSS feed plus extra C# / remote RSS query.
- Optional `DouExtraKeywords` and `DjinniExtraKeywords` arrays in appsettings.json. Terms are deduplicated, blank/oversized terms are ignored.
- Every feed has a separate query name, but entries retain canonical source `dou` or `djinni`; discovery provenance is persisted in PostgreSQL's `job_queries`.
- Data completeness still **PARTIAL**: each RSS query is capped and can miss older listings; category and remote filter semantics have not been independently verified in every returned item.
- Fresh full job descriptions are reused across sources when the existing entry is less than `DetailRefreshHours` old (default 24h). `CachedDetails` in each crawl source report makes reuse visible. Older details can be refreshed.
- Manual `--export-jsonl` emits title, link, description, preview, dates, unknown/known status and query provenance; `HasFullText` explicitly distinguishes full text from short snippets.
- Docker Compose runs a persistent PostgreSQL and a .NET worker on a machine controlled by the user; credentials stay in untracked `.env`.

## Verified public sources

- DOU C# supplementary RSS returned 25 entries through the GitHub Actions probe.
- Djinni C# supplementary RSS returned 100 entries.
- Counts above are distinct within each one-feed query, not evidence of complete cross-feed coverage.
- RSS endpoints are public and no authentication, CAPTCHA handling or bypass is used.

## Deployment

See [DEPLOY_DOCKER.md](DEPLOY_DOCKER.md). Production scans need an always-running user's computer or server. GitHub Actions only tests the container build, database connection and CLI; it is NOT a persistent collection server.

## Remaining limitations

- Full pagination of DOU and Djinni search listings is not covered by RSS.
- Unknown/closed vacancy status requires separate provider-grounded checks.
- Robota company detail API can cap at 100 and leave job detail unresolved.
- Work.ua returned HTTP 403 in prior tests and stays disabled.
- No direct ChatGPT connector to a local private Docker DB is installed; JSONL export can be uploaded for analysis.
