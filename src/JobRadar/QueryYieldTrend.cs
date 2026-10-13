namespace JobRadar;

/// <summary>
/// A comparison of marginal query counts from separate stored runs, NOT
/// an estimate of site-wide coverage or historic applicant eligibility.
/// </summary>
public sealed record QueryYieldTrendRow(
 string Query,string Provider,int RunsExamined,int VerifiedRuns,
 int? SumMarginalUrls,int? SumCurrentTargetBackendReviews,
 int? RunsWithNewUrls,int? RunsWithTargetReviews,
 string Assessment,IReadOnlyList<string> Signals);

public sealed record QueryYieldTrendReport(
 DateTimeOffset EvaluatedAt,int RunsExamined,
 DateTimeOffset? NewestRunEndedAt,DateTimeOffset? OldestRunEndedAt,
 IReadOnlyList<QueryYieldTrendRow> Queries,
 string Interpretation);

/// <summary>
/// Compares a bounded sequence of Phase 36 snapshots, newest first.
/// Incomparable query orders, incomplete data and overlapping runs
/// are unknown, never zero. No query configuration is changed.
/// </summary>
public static class QueryYieldTrend
{
 public static QueryYieldTrendReport Analyze(
  IReadOnlyList<QueryActionableYieldReport> newestFirst,
  DateTimeOffset evaluatedAt)
 {
  ArgumentNullException.ThrowIfNull(newestFirst);
  if(newestFirst.Count>10)
   throw new ArgumentOutOfRangeException(nameof(newestFirst),
    "Trend analysis is restricted to the latest ten runs.");
  for(var i=0;i<newestFirst.Count;i++)
  {
   var run=newestFirst[i];
   if(run.CrawlStartedAt>run.CrawlEndedAt || run.Queries is null ||
      run.CurrentDatabaseJobs<run.CurrentSnapshotJobs ||
      run.CurrentSnapshotOmitted!=run.CurrentDatabaseJobs-run.CurrentSnapshotJobs)
    throw new InvalidDataException("Malformed saved yield report");
   if(i>0 && run.CrawlEndedAt>newestFirst[i-1].CrawlEndedAt)
    throw new InvalidDataException("Runs must be ordered newest first");
   var distinct=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var query in run.Queries)
   {
    if(!string.Equals(DiscoveryGapAudit.ProviderForQuery(query.Query),
       query.Provider,StringComparison.OrdinalIgnoreCase) ||
       !distinct.Add(query.Provider+"\u001f"+query.Query))
     throw new InvalidDataException("Invalid or repeated query in saved yield report");
   }
  }

  if(newestFirst.Count==0)
   return new QueryYieldTrendReport(evaluatedAt,0,null,null,[],
    "No saved runs. No claims about published vacancies or query efficiency.");

  var newest=newestFirst[0];
  var result=new List<QueryYieldTrendRow>();
  foreach(var query in newest.Queries)
  {
   var signals=new SortedSet<string>(StringComparer.Ordinal);
   var expectedOrder=newest.Queries.Where(x=>
    string.Equals(x.Provider,query.Provider,StringComparison.OrdinalIgnoreCase))
    .Select(x=>x.Query).ToArray();
   int validRuns=0, marginalSum=0,targetSum=0,newRuns=0,targetRuns=0;
   bool comparable=true;
   for(var i=0;i<newestFirst.Count;i++)
   {
    var run=newestFirst[i];
    var providerOrder=run.Queries.Where(x=>
     string.Equals(x.Provider,query.Provider,StringComparison.OrdinalIgnoreCase))
     .Select(x=>x.Query).ToArray();
    if(!expectedOrder.SequenceEqual(providerOrder,StringComparer.OrdinalIgnoreCase))
    {
     signals.Add("QUERY_ORDER_OR_SET_CHANGED");
     comparable=false;
     continue;
    }
    var entry=run.Queries.First(x=>
     string.Equals(x.Provider,query.Provider,StringComparison.OrdinalIgnoreCase) &&
     string.Equals(x.Query,query.Query,StringComparison.OrdinalIgnoreCase));
    // The discovery loader's inclusive time windows must not share a
    // boundary: a single observation could otherwise belong to both runs.
    bool overlaps=(i>0 &&
      run.CrawlEndedAt>=newestFirst[i-1].CrawlStartedAt) ||
      (i+1<newestFirst.Count &&
      run.CrawlStartedAt<=newestFirst[i+1].CrawlEndedAt);
    if(overlaps)
    {
     signals.Add("OVERLAPPING_CRAWL_WINDOWS");
     comparable=false;
     continue;
    }
    if(run.CurrentSnapshotOmitted>0)
    {
     signals.Add("TRUNCATED_CURRENT_SNAPSHOT");
     comparable=false;
     continue;
    }
    if(entry.MarginalUrls is not int marginal ||
       entry.CurrentTargetBackendReview is not int target ||
       marginal<0 || target<0 || target>marginal)
    {
     signals.Add("UNVERIFIED_QUERY_YIELD");
     comparable=false;
     continue;
    }
    validRuns++;
    checked
    {
     marginalSum+=marginal;
     targetSum+=target;
    }
    if(marginal>0)newRuns++;
    if(target>0)targetRuns++;
   }

   if(newestFirst.Count<3)signals.Add("NEED_AT_LEAST_THREE_RUNS");
   if(!comparable)signals.Add("CROSS_RUN_TREND_UNVERIFIED");
   var qualified=comparable && validRuns==newestFirst.Count &&
    newestFirst.Count>=3;
   string assessment=!comparable?"UNVERIFIED":
    !qualified?"INSUFFICIENT_RUNS":
    targetRuns>=2?"REVIEW_RETAIN_TARGET_QUERY":
    marginalSum==0?"REVIEW_ZERO_MARGINAL_QUERY":
    targetSum==0?"REVIEW_LOW_TARGET_YIELD":
    "MIXED_TARGET_YIELD";
   // Sums are intentionally not unique URLs across time. They are the
   // same current-classifier perspective counted within separate runs.
   result.Add(new QueryYieldTrendRow(query.Query,query.Provider,
    newestFirst.Count,validRuns,
    qualified?marginalSum:null,qualified?targetSum:null,
    qualified?newRuns:null,qualified?targetRuns:null,
    assessment,signals.ToArray()));
  }

  return new QueryYieldTrendReport(evaluatedAt,newestFirst.Count,
   newest.CrawlEndedAt,newestFirst[^1].CrawlEndedAt,
   result.OrderBy(x=>x.Provider,StringComparer.OrdinalIgnoreCase)
    .ThenBy(x=>x.Query,StringComparer.OrdinalIgnoreCase).ToArray(),
   "At least 3 non-overlapping runs with identical per-provider query order "+
   "and complete current snapshots are needed for a comparison. Sums count "+
   "marginal occurrences PER RUN and may count the same URL on several days; "+
   "they are not unique cross-run job totals. Every run uses current stored "+
   "vacancy status, not an independent historic assessment. Source RSS caps, "+
   "geographic eligibility, job openings, and site-wide recall remain unknown. "+
   "Review labels are suggestions for human investigation, NEVER commands "+
   "to add, disable, or reorder provider queries or to submit applications.");
 }
}
