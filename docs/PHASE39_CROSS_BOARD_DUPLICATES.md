# Phase 39 — Conservative cross-board duplicate audit

## Why this matters

The owner ran JobRadar on 14 October 2026 and saved 549 vacancy records.
The review export contained the same **Strimco Middle .NET Developer**
posting at two separately stored URLs:

- DOU: \`https://jobs.dou.ua/companies/strimco/vacancies/362043/\`
- Djinni: \`https://djinni.co/jobs/831444-middle-net-developer/\`

The DOU title included \`в Strimco, Київ, віддалено\`, while Djinni had no
company value. The **actual full descriptions** in the owner-provided
\`triage-review-20261014-101131-2431775.jsonl\` normalized to the same
1,579-character text, despite formatting differences. Thus matching on
company/title alone would miss or misclassify the duplicate.

This phase finds *potential duplicate pairs* from strong textual evidence,
but does not group all similar-looking roles together or suppress either
vacancy's link. This is not a global deduplication claim.

## Offline command

\`\`\`powershell
dotnet run --project src/JobRadar/JobRadar.csproj -c Release -- --cross-board-duplicates
\`\`\`

- Reads only saved PostgreSQL vacancy snapshots, total stored count and
  operator-entered Applied/Inactive decisions.
- Reads up to \`min(ExportMaxRecords, 5000)\` current job records.
- Returns deterministic JSON with at most 100 candidate pairs, the
  normalized text similarity score, both original links, titles, companies,
  last-seen times and **independent per-URL owner decisions**.
- Exits 4 if the DB snapshot or pair output is truncated, rather than
  pretending all duplicates were inspected. Exits 0 for a complete clean
  audit even if no pairs qualify. Other exceptions exit 4.
- Does not contact job boards, crawl, query an AI service, alter records,
  change query ordering or transfer any Applied/Inactive state.

## Conservative evidence gate

A pair is emitted ONLY if ALL conditions hold:

1. Different provider identities; currently scoped to trusted DOU and
   Djinni URL patterns. Cross-platform Robota preview-only and WWR RSS-only
   evidence is too weak for this gate.
2. Both local records have full description text, with at least 450
   alphanumeric code points after punctuation/whitespace normalization.
3. Normalized core job titles are identical. DOU's trailing
   \`в Company, City, work format\` attribution is stripped to compare
   the base role title.
4. First 70 normalized characters of description text are identical,
   text lengths differ by no more than 15%, and unique
   12-character-shingle Dice similarity is at least 0.96.
5. Both links pass the existing explicit DOU/Djinni vacancy URL allowlist.

Matching text can occasionally be copied between genuinely different
jobs; a pair is always tagged \`POSSIBLE_DUPLICATE\` and needs a human
check. The report contains *pairs*, not transitive clusters. A posting
may be part of more than one pair, so pair count is not the number of
unique jobs saved. Avoid general claims like "two sites have only X
unique jobs".

## Employment-action isolation

An Applied marker on DOU remains **only** on that DOU URL. The Djinni
link may refer to the same employer, but cannot be marked Applied
without explicit user action or verified application history. Neither
job's status, fit, closing date or eligibility is inherited.

For human review, the report includes the saved owner-decision value
for each original URL. Provider open status itself may be stale and does
not indicate live availability. All unrelated jobs continue to appear
in existing Triage/Review reports.

## Acceptance and boundedness

Unit regressions cover the real Strimco two-board title/format pattern,
negative title-only match, differing descriptions, same-provider posts,
RSS/preview exclusion, false-link rejection, owner-action isolation,
missing input and truncation semantics.

An isolated PostgreSQL integration test saves two distinct full-text
posts and one owner Applied decision and verifies exact original URLs
and independent decisions in the output.

The existing WSL2 Draft-PR watcher should generate an immutable result
in private \`avysotsky/JobRadar-Relay-Queue/autotest-results/jobradar/
pr-N/FULL_SHA.json\`. Verify repo/PR/HEAD SHA/tree, all three stages
PASS, nonzero passed, failed zero, and cleanup 6/6 before merge.
Do not generate \`tasks-v2/\` tasks or enable GitHub Actions.

**Infra prerequisite:** issue #49 records that Phase 38 PR #48 received
three pre-snapshot \`INFRASTRUCTURE_ERROR\` results. A Phase 39 FAIL of the
same form does **not** prove a code defect. No PR is merged without
a tested exact source tree.
