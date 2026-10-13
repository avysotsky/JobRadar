namespace JobRadar;

/// <summary>
/// One canonical vacancy occurrence from job_discoveries during the time
/// window of a stored crawl. This is an observation, not a claim that every
/// vacancy on a provider was available through a capped RSS feed.
/// </summary>
public sealed record RssQueryObservation(string Provider,string QueryName,string Url);

public sealed record RssQueryRecallRow(
 string Query,string Provider,int ReportedReferences,int ObservedDistinctUrls,
 int? IncrementalUrls,int? AlreadySeenInEarlierQueries,
 bool MatchesSavedCrawl,string Status,IReadOnlyList<string> Signals);

public sealed record RssQueryRecallReport(
 DateTimeOffset StartedAt,DateTimeOffset EndedAt,
 IReadOnlyList<RssQueryRecallRow> Queries,
 IReadOnlyDictionary<string,int?> IncrementalProviderTotals);

/// <summary>
/// Query order is the crawler's stored source order. Newly observed references
/// are counted once for the earliest RSS query in that order within each
/// provider. Attribution is intentionally not based on title keyword matching.
/// </summary>
public static class RssQueryRecall
{
 public static string? CanonicalProvider(string query)=>
  query.Equals("dou",StringComparison.OrdinalIgnoreCase)||
  query.StartsWith("dou-rss-",StringComparison.OrdinalIgnoreCase)?"dou":
  query.Equals("djinni",StringComparison.OrdinalIgnoreCase)||
  query.StartsWith("djinni-rss-",StringComparison.OrdinalIgnoreCase)?"djinni":null;

 public static RssQueryRecallReport Analyze(
  CrawlReport report,IReadOnlyList<RssQueryObservation> observations)
 {
  ArgumentNullException.ThrowIfNull(report);
  ArgumentNullException.ThrowIfNull(observations);
  var sources=report.Sources.Where(s=>CanonicalProvider(s.Source)!=null)
   .GroupBy(s=>s.Source,StringComparer.OrdinalIgnoreCase)
   .Select(g=>g.First()).ToArray();
  var known=new HashSet<string>(sources.Select(x=>x.Source),
   StringComparer.OrdinalIgnoreCase);
  // Do not silently include events attributed to unrelated query names, even
  // if a caller supplies them. DB loader filters by names from the report.
  var candidates=observations.Where(x=>known.Contains(x.QueryName) &&
   CanonicalProvider(x.QueryName)==x.Provider &&
   !string.IsNullOrWhiteSpace(x.Url)).ToArray();
  var grouped=candidates.GroupBy(x=>x.QueryName,StringComparer.OrdinalIgnoreCase)
   .ToDictionary(g=>g.Key,g=>g.Select(x=>x.Url)
      .ToHashSet(StringComparer.OrdinalIgnoreCase),
    StringComparer.OrdinalIgnoreCase);
  var state=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
  var tainted=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var rows=new List<RssQueryRecallRow>();
  foreach(var source in sources)
  {
   var provider=CanonicalProvider(source.Source)!;
   if(!state.TryGetValue(provider,out var seen))
   {
    seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    state.Add(provider,seen);
   }
   if(!grouped.TryGetValue(source.Source,out var urls))
    urls=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   var signals=new SortedSet<string>(StringComparer.Ordinal);
   bool match=source.PagesFetched>0 && source.Status!="FAILED" &&
    urls.Count==source.ReferencesFound;
   if(!match)
   {
    signals.Add("REPORT_EVENT_COUNT_MISMATCH");
    tainted.Add(provider);
   }
   if(source.DroppedRecords>0)signals.Add("RSS_ITEMS_DISCARDED");
   if(source.Status=="FAILED")signals.Add("RSS_SOURCE_FAILED");
   if(source.Error?.Contains("RSS_EMPTY_UNVERIFIED",StringComparison.Ordinal)==true)
    signals.Add("RSS_EMPTY_UNVERIFIED");
   if(!match||tainted.Contains(provider))
    signals.Add("INCREMENTAL_ATTRIBUTION_UNVERIFIED");

   // Even with a mismatch, preserve the distinct observed URL set so later
   // rows cannot misleadingly treat earlier observed URLs as new.
   int incremental=0,alreadySeen=0;
   foreach(var url in urls)
    if(seen.Add(url))incremental++;else alreadySeen++;
   var verified=match && !tainted.Contains(provider);
   rows.Add(new RssQueryRecallRow(source.Source,provider,
    source.ReferencesFound,urls.Count,
    verified?incremental:null,verified?alreadySeen:null,
    match,source.Status,signals.ToArray()));
  }
  // Only publish a provider total when every included source query matches
  // its saved reference count. Failed and unverified queries are not zero.
  var totals=rows.GroupBy(x=>x.Provider,StringComparer.OrdinalIgnoreCase)
   .ToDictionary(g=>g.Key,g=>g.All(r=>r.IncrementalUrls.HasValue)
     ?(int?)g.Sum(r=>r.IncrementalUrls!.Value):null,
    StringComparer.OrdinalIgnoreCase);
  return new RssQueryRecallReport(report.StartedAt,report.EndedAt,rows,totals);
 }
}
