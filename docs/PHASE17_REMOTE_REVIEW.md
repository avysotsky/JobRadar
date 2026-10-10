# Phase 17 — Remote job review priorities (no destructive exclusions)

Phase 16 established a verified 43-row UTF-8/JSONL export from the October 10 WWR/Remotive scan. The output still showed zero \`LikelyFit\`, 10 \`NeedsReview\`, 33 \`Excluded\`, and \`CoverageStatus=PARTIAL\`. Phase 17 **does not fabricate positive opportunities or modify that conservative vacancy assessment**. It makes the human review queue more actionable.

## Output contract

Each new \`RemoteBoardCandidate\` JSONL item includes:
- \`ReviewPriority\`: \`DotNetEvidence\`, \`AdjacentEngineering\`, \`LocationVerification\`, \`UnknownStack\`, \`LowRelevance\` or \`NotApplicable\`.
- \`ReviewEvidence\`: short authored justification, **not copied full provider descriptions**.

These tiers are independent of the pre-existing \`LikelyFit / NeedsReview / Excluded\` buckets and scores. \`Excluded\` and \`LikelyFit\` receive \`NotApplicable\`. \`NeedsReview\` stays intact, including low-priority items; none are silently dropped.

Priority rules:
- **DotNetEvidence:** an engineering job mentions C#/.NET in its title or received role text, subject to verification of main technology requirements, level, spoken English and Ukraine eligibility.
- **AdjacentEngineering:** AI-agent development or AI-assisted coding/automation jobs without a clear .NET primary stack; secondary exploration only.
- **LocationVerification:** Remotive location metadata fails a strict worldwide/Ukraine allowlist or WWR RSS contains a **remote-US location cue**. This flags a geographic verification task; it is **not** proof of the legal location policy. A company's US headquarters alone is never interpreted as US-only hiring.
- **UnknownStack:** an engineering role with incomplete tech-stack evidence.
- **LowRelevance:** titles showing a distinct specialization (e.g. program manager, application security engineer, data scientist), or obvious non-.NET technology focus. Items remain available in the complete audit.

All checks use the **entire text received in memory** in the live scan. For WWR, the full job posting remains **unverified**, even though the RSS text is now fully inspected. The \`FullDescription\` remains deliberately excluded from JSONL.

## Offline audit of existing verified export

No provider requests, no credentials, and **no PostgreSQL access**:

\`\`\`powershell
Set-Location "D:\projects\jobradar"
dotnet run --project ".\src\JobRadar\JobRadar.csproj" -c Release -- "--remote-boards-review=reports\remote-boards-verified-20261010-200319.jsonl"
\`\`\`

The command validates UTF-8/JSONL first and prints **only the existing NeedsReview items**, sorted by review priority and score. One compact JSONL row per item shows source, original URL, title, bucket, review priority, score, published date and conservative status. A summary on stderr shows exact counts by priority. All entries remain marked \`UNVERIFIED_OPEN_AND_WORK_LOCATION\`. No provider descriptions are printed.

For **Phase 17 exported files**, the live review priorities were computed from the full *received* text and preserved in JSONL. For the **Phase 16 archived file**, where no \`ReviewPriority\` field exists, the tool recomputes **conservative queue hints from the serialized excerpts**. This cannot uncover requirements beyond the excerpts; avoid definitive judgments until consulting full provider pages.

## Unified opportunity preview

\`--opportunities-preview --remote-boards-file=PATH\` now emits a \`RemoteReviewPriority\` string for remote-board employment records, preserving the ranking already computed by the live scan or deriving an offline hint for older snapshots. No changes are made to freelance scores, database writes, or scheduled collectors.

## Regression coverage

Synthetic test cases reflect the first live scan's failure modes without storing third-party posting descriptions: unrelated manager/security/data-science positions, AI-agent roles, generic backend without .NET, and a location-limited Remotive role mentioning C#. Additional coverage checks region cues in WWR RSS, headquarters versus work eligibility, middle .NET role, full received text after the excerpt cut-off, preservation of \`Excluded\`, offline JSONL safety and integrated preview.

**No new live HTTP requests are required for acceptance.**

Use isolated \`JOBRADAR_TEST_DB=jobradar_test\` and \`scripts/validate-local.ps1 -RequirePostgres\` on the owner's Windows host. Keep GitHub Actions **OFF** and PR **Draft** until owner provides test results.

## Explicitly out of scope

- Verifying that the job is currently open
- Confirmation of legal eligibility to work from Ukraine
- Fetching/retaining full WWR posting content
- Filling gaps with invented source text or ranks
- Eliminating listings from the complete audit solely because .NET was absent from the truncated RSS snapshot
- Bidding, applying, or automatically persisting third-party data

Future: a separate source-compliant full-posting evidence workflow with geographic eligibility review and updated matching calibration, conditional on each provider's permitted access methods and terms.
