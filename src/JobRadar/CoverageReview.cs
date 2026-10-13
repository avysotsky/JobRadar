namespace JobRadar;

/// <summary>
/// An observed-source triage view. This ranks actionable *evidence in stored
/// crawl reports*, not true vacancy-market completeness or job eligibility.
/// </summary>
public sealed record SourceCoverageReview(
 string Source,string Provider,string Priority,
 DateTimeOffset LastObservedAt,bool PresentInNewestRun,int RunsObserved,
 string LatestStatus,int LatestReferences,int? LatestReportedTotal,
 int? LatestRawRecords,int? LatestDroppedRecords,
 int? PriorObservedPeak,int? UnobservedFromFirstTotal,
 IReadOnlyList<string> Signals);

public sealed record CoverageReviewReport(
 DateTimeOffset EvaluatedAt,int ReportsExamined,DateTimeOffset? NewestReportAt,
 IReadOnlyList<SourceCoverageReview> Sources);

/// <summary>
/// Input is newest-first, as read from crawl_runs ordered by insertion id.
/// Sources omitted from a run are not considered to have returned zero results.
/// </summary>
public static class CoverageReview
{
 public static CoverageReviewReport Analyze(
  IReadOnlyList<CrawlReport> recentNewestFirst,DateTimeOffset now)
 {
  ArgumentNullException.ThrowIfNull(recentNewestFirst);
  var samples=new Dictionary<string,List<(int Index,DateTimeOffset At,SourceResult Source)>>(
   StringComparer.OrdinalIgnoreCase);
  for(var i=0;i<recentNewestFirst.Count;i++)
  {
   var report=recentNewestFirst[i];
   foreach(var source in report.Sources)
   {
    if(ProviderFor(source.Source) is null)continue;
    if(!samples.TryGetValue(source.Source,out var history))
    {
     history=[];
     samples.Add(source.Source,history);
    }
    // The crawler emits one result per configured query. If an old report
    // contains duplicate entries, do not count them as independent runs.
    if(history.Count>0 && history[^1].Index==i)continue;
    history.Add((i,report.EndedAt,source));
   }
  }
  var reviewed=new List<SourceCoverageReview>();
  foreach(var (name,history) in samples)
  {
   var latest=history[0];
   var source=latest.Source;
   var provider=ProviderFor(name)!;
   var signals=new SortedSet<string>(StringComparer.Ordinal);
   bool present=latest.Index==0;
   if(!present)signals.Add("NOT_IN_NEWEST_RUN");
   if(source.Status=="FAILED")signals.Add("LATEST_SCAN_FAILED");
   if(source.DetailsFailed>0)signals.Add("DETAIL_FETCH_FAILURES");
   if(source.DroppedRecords>0)signals.Add("RAW_ITEMS_DISCARDED");

   var gap=source.ReportedTotal.HasValue
    ?Math.Max(0,source.ReportedTotal.Value-source.ReferencesFound)
    :(int?)null;
   if(gap>0)signals.Add("UNOBSERVED_FROM_REPORTED_TOTAL");
   if(source.ReportedTotal.HasValue &&
      source.ReferencesFound>source.ReportedTotal.Value)
    signals.Add("REFERENCES_EXCEED_REPORTED_TOTAL");
   if(now-latest.At>TimeSpan.FromHours(48))
    signals.Add("LAST_OBSERVATION_OVER_48H");

   // The former peak is an *observed* baseline, not a site-wide expected
   // count. A legitimate drop in the market can also trigger this signal.
   var previous=history.Skip(1).Where(x=>x.Source.Status!="FAILED")
    .Select(x=>x.Source.ReferencesFound).ToArray();
   int? priorPeak=previous.Length==0?null:previous.Max();
   if(priorPeak>=10 && source.Status!="FAILED" &&
      (long)source.ReferencesFound*2<priorPeak.Value)
    signals.Add("OBSERVED_REFERENCES_DOWN_OVER_50_PERCENT");

   var error=source.Error??"";
   if(provider=="Robota")
   {
    foreach(var signal in RobotaCoverageEvidence.From(source).Signals)
    {
     if(signal is RobotaCoverageSignals.VerifiedZero)continue;
     signals.Add(signal);
    }
    if(error.Contains("full-text disabled",StringComparison.OrdinalIgnoreCase))
     signals.Add("FULL_TEXT_ENDPOINT_PAUSED");
   }
   else
   {
    // RSS feeds have no independent total and no known exhaustive paging.
    signals.Add("RSS_NOT_EXHAUSTIVE");
    if(error.Contains("RSS_EMPTY_UNVERIFIED",StringComparison.Ordinal))
     signals.Add("RSS_EMPTY_UNVERIFIED");
    if(error.Contains("RSS_REFERENCE_LOSS",StringComparison.Ordinal))
     signals.Add("RSS_REFERENCE_LOSS");
   }

   // Overall PARTIAL is normal for capped RSS and not equivalent to a
   // broken connector. Rank evidence of failures/loss ahead of RSS caps.
   var critical=signals.Contains("LATEST_SCAN_FAILED");
   var actionable=signals.Overlaps(new[]
   {
    "DETAIL_FETCH_FAILURES","RAW_ITEMS_DISCARDED",
    "UNOBSERVED_FROM_REPORTED_TOTAL","REFERENCES_EXCEED_REPORTED_TOTAL",
    "OBSERVED_REFERENCES_DOWN_OVER_50_PERCENT",
    "RSS_EMPTY_UNVERIFIED","RSS_REFERENCE_LOSS",
    RobotaCoverageSignals.FetchFailed,RobotaCoverageSignals.ParseFailed,
    RobotaCoverageSignals.EmptyBeforeTotal,RobotaCoverageSignals.EmptyUnverified,
    RobotaCoverageSignals.PageOverlap,RobotaCoverageSignals.TotalDrift
   });
   var priority=critical?"P0":actionable?"P1":
    !present||signals.Contains("LAST_OBSERVATION_OVER_48H")||
    provider!="Robota"||source.Status!="QUERY_RECONCILED"?"P2":"P3";

   reviewed.Add(new SourceCoverageReview(
    name,provider,priority,latest.At,present,history.Count,
    source.Status,source.ReferencesFound,source.ReportedTotal,
    source.RawRecords,source.DroppedRecords,priorPeak,gap,
    signals.ToArray()));
  }
  return new CoverageReviewReport(now,recentNewestFirst.Count,
   recentNewestFirst.Count==0?null:recentNewestFirst[0].EndedAt,
   reviewed.OrderBy(x=>x.Priority,StringComparer.Ordinal)
    .ThenBy(x=>x.Provider,StringComparer.Ordinal)
    .ThenBy(x=>x.Source,StringComparer.OrdinalIgnoreCase).ToArray());
 }

 private static string? ProviderFor(string source)=>
  source=="dou"||source.StartsWith("dou-rss-",StringComparison.OrdinalIgnoreCase)
   ?"DOU":
  source=="djinni"||source.StartsWith("djinni-rss-",StringComparison.OrdinalIgnoreCase)
   ?"Djinni":
  source.StartsWith("robota-api-",StringComparison.OrdinalIgnoreCase)
   ?"Robota":
  source.Equals("wwr-backend",StringComparison.OrdinalIgnoreCase)||
  source.Equals("wwr-programming",StringComparison.OrdinalIgnoreCase)
   ?"WWR":null;
}
