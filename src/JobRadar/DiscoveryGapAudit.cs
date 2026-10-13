namespace JobRadar;

/// <summary>One stored discovery event; NOT independent evidence that the provider has an open vacancy.</summary>
public sealed record DiscoveryGapObservation(string Provider,string Query,string Url);

public sealed record DiscoveryGapQuery(
 string Query,string Provider,string Priority,string SourceStatus,
 int ReportedReferences,int ObservedDistinctUrls,bool MatchesStoredDiscoveries,
 int? NewToEarlierQueries,int? OverlapWithEarlierQueries,
 int? UnobservedFromReportedTotal,int? DroppedRecords,
 IReadOnlyList<string> Signals);

public sealed record DiscoveryGapReport(
 DateTimeOffset StartedAt,DateTimeOffset EndedAt,
 IReadOnlyList<DiscoveryGapQuery> Queries,
 IReadOnlyDictionary<string,int?> PerProviderObservedUniqueUrls,
 string Interpretation);

/// <summary>
/// Read-only reconciliation for DOU/Djinni RSS, Robota search JSON and WWR RSS.
/// Missing event counts are evidence of a persisted-run discrepancy, not a list
/// of vacancies the employer or provider failed to publish.
/// </summary>
public static class DiscoveryGapAudit
{
 public static string? ProviderForQuery(string? query)
 {
  if(string.IsNullOrWhiteSpace(query))return null;
  if(query.Equals("dou",StringComparison.OrdinalIgnoreCase) ||
     query.StartsWith("dou-rss-",StringComparison.OrdinalIgnoreCase))
   return "dou";
  if(query.Equals("djinni",StringComparison.OrdinalIgnoreCase) ||
     query.StartsWith("djinni-rss-",StringComparison.OrdinalIgnoreCase))
   return "djinni";
  if(query.StartsWith("robota-api-",StringComparison.OrdinalIgnoreCase) &&
     query.Length>"robota-api-".Length)
   return "robota";
  if(query.Equals("wwr-backend",StringComparison.OrdinalIgnoreCase) ||
     query.Equals("wwr-programming",StringComparison.OrdinalIgnoreCase))
   return "wwr";
  return null;
 }

 public static DiscoveryGapReport Analyze(
  CrawlReport report,IReadOnlyList<DiscoveryGapObservation> observations)
 {
  ArgumentNullException.ThrowIfNull(report);
  ArgumentNullException.ThrowIfNull(observations);
  if(report.Sources is null || report.StartedAt>report.EndedAt)
   throw new InvalidDataException("Malformed stored crawl report");
  var sources=report.Sources.Where(x=>ProviderForQuery(x.Source)!=null).ToArray();
  if(sources.Select(x=>x.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=sources.Length)
   throw new InvalidDataException("Duplicate provider query in crawl report");

  var allowed=new HashSet<string>(sources.Select(x=>x.Source),StringComparer.OrdinalIgnoreCase);
  var events=observations.Where(x=>allowed.Contains(x.Query) &&
   string.Equals(ProviderForQuery(x.Query),x.Provider,StringComparison.OrdinalIgnoreCase) &&
   !string.IsNullOrWhiteSpace(x.Url))
   .GroupBy(x=>x.Query,StringComparer.OrdinalIgnoreCase)
   .ToDictionary(x=>x.Key,x=>x.Select(e=>e.Url).ToHashSet(StringComparer.OrdinalIgnoreCase),
    StringComparer.OrdinalIgnoreCase);

  var seen=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
  var tainted=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var rows=new List<DiscoveryGapQuery>();
  foreach(var source in sources)
  {
   var provider=ProviderForQuery(source.Source)!;
   if(!seen.TryGetValue(provider,out var known))
   {
    known=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    seen.Add(provider,known);
   }
   if(!events.TryGetValue(source.Source,out var urls))
    urls=new HashSet<string>(StringComparer.OrdinalIgnoreCase);

   var signals=new SortedSet<string>(StringComparer.Ordinal);
   var fetched=source.PagesFetched>0 && source.Status!="FAILED";
   var matched=fetched && source.ReferencesFound>=0 && urls.Count==source.ReferencesFound;
   if(!fetched)signals.Add("SOURCE_FETCH_UNVERIFIED");
   if(!matched)signals.Add("DISCOVERY_EVENT_COUNT_MISMATCH");
   if(source.DroppedRecords>0)signals.Add("RAW_RECORDS_DROPPED");
   if(source.DetailsFailed>0)signals.Add("DETAIL_FETCH_FAILURES");
   if(source.Error?.Contains("RSS_EMPTY_UNVERIFIED",StringComparison.Ordinal)==true)
    signals.Add("RSS_EMPTY_UNVERIFIED");
   if(source.Error?.Contains("RSS_REFERENCE_LOSS",StringComparison.Ordinal)==true)
    signals.Add("RSS_REFERENCE_LOSS");
   if(source.Error?.Contains("SEARCH_FETCH_FAILED",StringComparison.Ordinal)==true)
    signals.Add("SEARCH_FETCH_FAILED");
   if(source.Error?.Contains("SEARCH_PARSE_FAILED",StringComparison.Ordinal)==true)
    signals.Add("SEARCH_PARSE_FAILED");
   if(provider!="robota")signals.Add("RSS_NOT_EXHAUSTIVE");

   int? gap=provider=="robota" && source.ReportedTotal.HasValue
    ?Math.Max(0,source.ReportedTotal.Value-source.ReferencesFound):null;
   if(gap>0)signals.Add("UNOBSERVED_FROM_REPORTED_TOTAL");
   if(provider=="robota" && source.ReportedTotal.HasValue &&
      source.ReferencesFound>source.ReportedTotal.Value)
    signals.Add("UNIQUE_IDS_EXCEED_REPORTED_TOTAL");
   if(provider=="robota" && source.ReportedTotal is null)
    signals.Add("REPORTED_TOTAL_UNKNOWN");
   if(provider=="robota" &&
      (source.Error?.Contains("search pages overlap",StringComparison.OrdinalIgnoreCase)==true ||
       source.Error?.Contains(RobotaCoverageSignals.TotalDrift,StringComparison.Ordinal)==true))
    signals.Add("ROBOTA_PAGE_OR_TOTAL_DRIFT");

   if(!matched)tainted.Add(provider);
   int newUrls=0,overlap=0;
   foreach(var url in urls)
   {
    if(known.Add(url))newUrls++;else overlap++;
   }
   bool verified=matched && !tainted.Contains(provider);
   if(!verified)signals.Add("CROSS_QUERY_ATTRIBUTION_UNVERIFIED");

   var priority=!fetched?"P0":
    !matched || source.DroppedRecords>0 || source.DetailsFailed>0 ||
    gap>0 || signals.Contains("RSS_EMPTY_UNVERIFIED") ||
    signals.Contains("RSS_REFERENCE_LOSS") ||
    signals.Contains("SEARCH_FETCH_FAILED") ||
    signals.Contains("SEARCH_PARSE_FAILED") ||
    signals.Contains("ROBOTA_PAGE_OR_TOTAL_DRIFT") ||
    signals.Contains("UNIQUE_IDS_EXCEED_REPORTED_TOTAL")?"P1":
    provider!="robota" || source.Status!="QUERY_RECONCILED"?"P2":"P3";

   rows.Add(new DiscoveryGapQuery(source.Source,provider,priority,source.Status,
    source.ReferencesFound,urls.Count,matched,
    verified?newUrls:null,verified?overlap:null,gap,source.DroppedRecords,
    signals.ToArray()));
  }
  var totals=rows.GroupBy(x=>x.Provider,StringComparer.OrdinalIgnoreCase)
   .ToDictionary(x=>x.Key,x=>x.All(r=>r.NewToEarlierQueries.HasValue)
      ?(int?)x.Sum(r=>r.NewToEarlierQueries!.Value):null,
     StringComparer.OrdinalIgnoreCase);
  return new DiscoveryGapReport(report.StartedAt,report.EndedAt,
   rows.OrderBy(x=>x.Priority,StringComparer.Ordinal)
    .ThenBy(x=>x.Provider,StringComparer.Ordinal)
    .ThenBy(x=>x.Query,StringComparer.OrdinalIgnoreCase).ToArray(),
   totals,
   "Counts reconcile saved query references with discovery events in one run. "+
   "Marginal counts depend on original crawl query order and are NOT vacancies "+
   "newly posted by the employer. RSS feeds are capped; Robota totals are query "+
   "snapshots, not market totals. No evidence here proves provider-wide recall, "+
   "current availability, Ukraine eligibility or closure.");
 }
}
