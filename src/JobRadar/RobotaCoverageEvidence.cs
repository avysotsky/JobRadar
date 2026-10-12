namespace JobRadar;

/// <summary>
/// Additive, query-scoped evidence for Robota search pagination. The unobserved
/// count compares unique URLs against the FIRST provider total; it does not
/// prove that specific vacancies were lost or that the provider was stable.
/// </summary>
public sealed record RobotaCoverageEvidence(
 int RepeatedAcrossPages,
 int? UnobservedFromReportedTotal,
 IReadOnlyList<string> Signals);

/// <summary>
/// Stable machine-readable diagnostics; not a claim of whole-board coverage.
/// Several signals may be present for the same crawl.
/// </summary>
public static class RobotaCoverageSignals
{
 public const string VerifiedZero="VERIFIED_ZERO_RESULTS";
 public const string EmptyUnverified="EMPTY_FIRST_PAGE_UNVERIFIED";
 public const string EmptyBeforeTotal="EMPTY_PAGE_BEFORE_TOTAL";
 public const string FetchFailed="SEARCH_FETCH_FAILED";
 public const string ParseFailed="SEARCH_PARSE_FAILED";
 public const string RawRecordsDropped="RAW_RECORDS_DROPPED";
 public const string PageOverlap="CROSS_PAGE_OVERLAP";
 public const string TotalDrift="REPORTED_TOTAL_DRIFT";
 public const string TotalMissing="REPORTED_TOTAL_UNKNOWN";
 public const string Unobserved="UNOBSERVED_FROM_REPORTED_TOTAL";
 public const string ExcessIds="UNIQUE_IDS_EXCEED_REPORTED_TOTAL";
 public const string PageLimit="PAGE_LIMIT_REACHED";
}
