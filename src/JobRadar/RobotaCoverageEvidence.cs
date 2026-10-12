namespace JobRadar;

/// <summary>
/// Read-only, derived diagnostics for ONE Robota query from an existing
/// persisted crawl report. No provider access and no inferred vacancy closure.
/// </summary>
public sealed record RobotaCoverageEvidence(
 string Source,string Status,int PagesFetched,int UniqueReferences,
 int RawRecords,int DroppedWithinPages,int? ReportedTotal,
 int? UnobservedFromReportedTotal,IReadOnlyList<string> Signals)
{
 public static RobotaCoverageEvidence From(SourceResult result)
 {
  if(!result.Source.StartsWith("robota-api-",StringComparison.OrdinalIgnoreCase))
   throw new ArgumentException("Expected a Robota API query",nameof(result));
  var signals=new SortedSet<string>(StringComparer.Ordinal);
  var error=result.Error??"";
  var raw=result.RawRecords??0;
  var dropped=result.DroppedRecords??0;
  var gap=result.ReportedTotal.HasValue
   ?Math.Max(0,result.ReportedTotal.Value-result.ReferencesFound)
   :(int?)null;

  // Zero is confirmed only when the provider explicitly declared total=0,
  // returned no raw documents, and this query was reconciled.
  if(result.ReportedTotal==0 && raw==0 &&
     result.ReferencesFound==0 && result.Status=="QUERY_RECONCILED")
   signals.Add(RobotaCoverageSignals.VerifiedZero);
  if(error.Contains(RobotaCoverageSignals.FetchFailed,StringComparison.Ordinal))
   signals.Add(RobotaCoverageSignals.FetchFailed);
  if(error.Contains(RobotaCoverageSignals.ParseFailed,StringComparison.Ordinal))
   signals.Add(RobotaCoverageSignals.ParseFailed);
  if(error.Contains(RobotaCoverageSignals.EmptyUnverified,StringComparison.Ordinal))
   signals.Add(RobotaCoverageSignals.EmptyUnverified);
  if(error.Contains(RobotaCoverageSignals.EmptyBeforeTotal,StringComparison.Ordinal))
   signals.Add(RobotaCoverageSignals.EmptyBeforeTotal);
  if(error.Contains(RobotaCoverageSignals.TotalDrift,StringComparison.Ordinal)||
     error.Contains("reported total changed between pages",StringComparison.OrdinalIgnoreCase)||
     error.Contains("reported total missing on subsequent page",StringComparison.OrdinalIgnoreCase)||
     error.Contains("reported total first appeared",StringComparison.OrdinalIgnoreCase))
   signals.Add(RobotaCoverageSignals.TotalDrift);
  if(error.Contains("search pages overlap",StringComparison.OrdinalIgnoreCase))
   signals.Add(RobotaCoverageSignals.PageOverlap);
  if(dropped>0)signals.Add(RobotaCoverageSignals.RawRecordsDropped);
  if(result.ReportedTotal is null)signals.Add(RobotaCoverageSignals.TotalMissing);
  if(gap>0)signals.Add(RobotaCoverageSignals.Unobserved);
  if(result.ReportedTotal.HasValue &&
     result.ReferencesFound>result.ReportedTotal.Value)
   signals.Add(RobotaCoverageSignals.ExcessIds);

  return new RobotaCoverageEvidence(result.Source,result.Status,
   result.PagesFetched,result.ReferencesFound,raw,dropped,
   result.ReportedTotal,gap,signals.ToArray());
 }
}

/// <summary>Stable codes in read-only reports, not assertions of site-wide coverage.</summary>
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
}
