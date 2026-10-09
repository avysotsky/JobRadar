# Phase 3: Djinni RSS and Jooble

## Djinni

- Search RSS URL: https://djinni.co/jobs/rss/?primary_keyword=.NET&employment=remote
- GitHub Actions live smoke: 54 references found; one full description 1,891 characters.
- Feed completeness and query-category validation are unproven. Status must remain PARTIAL.
- Djinni RSS is read instead of unreliable guessed HTML pagination. Each detail is fetched separately.

## Jooble

- Official documentation: https://help.jooble.org/en/support/solutions/articles/60001448238-rest-api-documentation
- Endpoint: POST https://ua.jooble.org/api/{regional-key} (key supplied as JOOBLE_API_KEY in environment).
- Free API quota: 500 requests over lifetime of each regional key, not monthly.
- Command: dotnet run --project src/JobRadar/JobRadar.csproj -- --jooble-once
- Mandatory JOOBLE_API_PRIOR_USED (include requests made outside JobRadar) and JOOBLE_MAX_NEW_REQUESTS.
- Atomic PostgreSQL api_request_budget reservation happens BEFORE any request and is retained even on failures.
- Only first page per configured keyword is fetched: default 2 keywords; no unsupervised scheduled calls.
- Jooble snippet is saved to jobs.preview and title/company/url to jobs; description remains blank, published time unknown.
- Do not mistake updated timestamp for publication date or Jooble snippet for complete job description.
- Source can still have more results than the first page; report status PARTIAL_SNIPPET_ONLY by design.
- The API key is never stored in code, JSON configuration, GitHub Actions or PostgreSQL.
- No live key was supplied; validation is through synthetic HTTP responses and PostgreSQL integration tests.

## Remaining

- Djinni: verify RSS keyword taxonomy and detect incorrect provider-side filters.
- Jooble: permissioned full-description acquisition and employer-link checking.
- DOU: full feed coverage analysis.
- Work.ua: supported, authorized feed needed; current HTTP 403 blocks automated extraction.
