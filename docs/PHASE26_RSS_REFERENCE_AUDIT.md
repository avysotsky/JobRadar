# Phase 26 — DOU / Djinni RSS reference integrity audit

## Problem

The default and supplemental DOU/Djinni RSS feeds are capped, and neither
provider offers an independently verified complete-page total through these
RSS endpoints. JobRadar correctly labels these source runs `PARTIAL`, but
previously the parsers discarded invalid RSS items and in-feed duplicates
without recording how many original `<item>` entries were received.

An empty, syntactically valid RSS response is **not** proof that the employer
board contains zero matching opportunities.

## Implementation

- `IRssFeedSource.ParseFeed` returns the original feed item count,
  canonical usable unique jobs, unusable items and repeated canonical URLs.
- All four adapters participate: `DouSource`, `DouKeywordFeed`,
  `DjinniSource`, and `DjinniKeywordFeed`.
- The existing `ParseListings` contract remains functional. Keyword
  variants reuse the canonical parent parser and preserve canonical source
  keys (`dou`, `djinni`) and distinct search query names.
- `SourceResult.RawRecords` and `DroppedRecords` now report RSS counts.
  `DroppedRecords = UnusableItems + DuplicateItems` within each feed.
  The `Error` includes the two individual counts if either is nonzero.
- An empty RSS includes `RSS_EMPTY_UNVERIFIED` and remains `PARTIAL`.
- Fetch or malformed-XML failures have **null** RSS raw/drop counts:
  unknown must not silently become zero.
- The bounded, manual `--smoke-feed-variants` diagnostic includes the
  same counters and returns a degraded result if a feed loses any items.

An RSS item classified as unusable has a missing/unsupported provider link
or a title shorter than three characters. Duplicates are repeats of the
same canonical provider URL **within one feed**. Repeated jobs from
different keyword feeds are allowed and preserve discovery provenance
in `job_queries`; recent full text remains cached.

## Coverage semantics

Examples (all data below synthetic):

| Received RSS items | Usable unique refs | Unusable | Duplicates | Meaning |
|---:|---:|---:|---:|---|
| 4 | 1 | 2 | 1 | Explicit in-feed loss, query still PARTIAL |
| 0 | 0 | 0 | 0 | Empty/unverified feed, **not** a verified zero-vacancy result |
| Unknown | 0 | Unknown | Unknown | Source fetch/XML failure, not zero hits |
| 1 + 1 from separate queries | 1 canonical job | 0 | 0 | Normal cross-query repeat, two provenance entries |

Raw/drop counts do **not** establish provider-wide coverage, actual vacancy
availability, location eligibility or that older jobs absent from capped
RSS are closed. No pagination, login, CAPTCHA handling or 403 bypass is
introduced.

## Validation and operation

The new xUnit fixtures exercise both providers, default and keyword
variants, malformed entries, duplicates, empty feeds, HTTP/XML failures,
PostgreSQL persistence, and cross-query discovery history.

Build and test remain via the **existing** private HP Trusted Git Relay v2
`restore → build → test` on an exact immutable commit. Do not
restore GitHub Actions. No Task Scheduler changes or automatic applications.

The change affects only parsing, source-level metrics, and the manual feed
smoke diagnostic; no new database migrations, external API contracts or
secrets are required.

## Next gaps

- RSS caps and off-feed vacancies remain unobservable without another
  authorized source; no completeness claim is made.
- A real permissioned/provider-backed end-to-end smoke could compare the
  observed RSS references with published result counts, if such an API or
  authorized method becomes available.
