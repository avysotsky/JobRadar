namespace JobRadar;

/// <summary>
/// A human-readable, non-executing recommendation backed by stored evidence.
/// Priority orders investigations, not the quality or availability of vacancies.
/// </summary>
public sealed record CoverageAction(
 string Priority,string Code,string Provider,string? Query,
 string RecommendedCheck,IReadOnlyList<string> Evidence);

public sealed record CoverageActionPlanReport(
 DateTimeOffset EvaluatedAt,DateTimeOffset? LatestCrawlAt,
 int ReportsExamined,int VacanciesEvaluated,long VacanciesStored,
 long VacanciesOmitted,bool CompleteVacancySample,
 IReadOnlyList<CoverageAction> Actions,string Interpretation);

/// <summary>
/// Connect existing Phase 27, 33 and 34 reports without network requests,
/// automatic source changes, or speculative claims of provider-wide recall.
/// </summary>
public static class CoverageActionPlanner
{
 public static CoverageActionPlanReport Analyze(
  CoverageReviewReport coverage,DiscoveryGapReport? gaps,
  VacancyFunnelReport funnel,DateTimeOffset now)
 {
  ArgumentNullException.ThrowIfNull(coverage);
  ArgumentNullException.ThrowIfNull(funnel);
  if(coverage.Sources is null || funnel.Sources is null)
   throw new InvalidDataException("Incomplete stored audit");
  if(gaps is not null && gaps.Queries is null)
   throw new InvalidDataException("Incomplete discovery audit");
  // Never splice discovery events from an unrelated or different crawl run.
  if(gaps is not null && coverage.NewestReportAt!=gaps.EndedAt)
   throw new InvalidDataException("Discovery gaps do not match latest saved crawl");
  var actions=new List<CoverageAction>();
  var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  void Add(string priority,string code,string provider,string? query,
   string guidance,params string[] evidence)
  {
   if(!unique.Add(provider+"\u001f"+(query??"")+"\u001f"+code))return;
   actions.Add(new CoverageAction(priority,code,provider,query,guidance,
    evidence.Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray()));
  }

  if(coverage.NewestReportAt is null)
   Add("P1","NO_SAVED_CRAWL","ALL",null,
    "Inspect local database and configured authorized collector; a missing report is not zero published vacancies.",
    "NO_SAVED_CRAWL");
  else if(now-coverage.NewestReportAt>TimeSpan.FromHours(48))
   Add("P1","STALE_COLLECTOR_EVIDENCE","ALL",null,
    "Verify the owner's scheduled collector and review whether a permitted run is needed; do not start crawling from this audit.",
    "NEWEST_CRAWL_OVER_48H");

  var queries=gaps?.Queries.ToDictionary(x=>x.Query,
   StringComparer.OrdinalIgnoreCase) ??
   new Dictionary<string,DiscoveryGapQuery>(StringComparer.OrdinalIgnoreCase);
  foreach(var item in coverage.Sources)
  {
   var provider=DiscoveryGapAudit.ProviderForQuery(item.Source);
   if(provider is null)continue;
   var query=item.Source;
   queries.TryGetValue(query,out var gap);
   if(!item.PresentInNewestRun)
   {
    Add("P2","QUERY_NOT_IN_LATEST_SCAN",provider,query,
     "Compare enabled source/query settings with the latest crawl; absence means not scanned, not zero vacancies.",
     "NOT_IN_NEWEST_RUN");
    continue;
   }
   if(item.LatestStatus=="FAILED" || gap?.Priority=="P0")
   {
    Add("P0","SOURCE_FETCH_FAILED",provider,query,
     "Inspect the stored connector error and provider contract; respect rate limits, permissions and HTTP 403 prohibitions.",
     "LATEST_SCAN_FAILED");
    continue;
   }
   if(gap is null)
   {
    Add("P1","DISCOVERY_RECONCILIATION_MISSING",provider,query,
     "Latest query lacks matching discovery audit data; verify bounded saved-run reconciliation before interpreting coverage.",
     "DISCOVERY_AUDIT_UNAVAILABLE");
   }
   else
   {
    if(!gap.MatchesStoredDiscoveries)
     Add("P1","DISCOVERY_COUNT_MISMATCH",provider,query,
      "Inspect job_discoveries persistence and crawl-window isolation; do not interpret a missing event as a closed or absent vacancy.",
      "DISCOVERY_EVENT_COUNT_MISMATCH");
    if(gap.DroppedRecords>0 ||
       gap.Signals.Contains("RAW_RECORDS_DROPPED") ||
       gap.Signals.Contains("RSS_REFERENCE_LOSS"))
     Add("P1","SOURCE_REFERENCE_LOSS",provider,query,
      "Inspect rejected IDs, invalid links and duplicates using offline parser fixtures; do not increase provider request volume.",
      "RAW_RECORDS_DROPPED");
    if(provider=="robota" &&
       (gap.UnobservedFromReportedTotal>0 ||
        gap.Signals.Contains("ROBOTA_PAGE_OR_TOTAL_DRIFT") ||
        gap.Signals.Contains("UNIQUE_IDS_EXCEED_REPORTED_TOTAL")))
     Add("P1","ROBOTA_SEARCH_PAGING_UNVERIFIED",provider,query,
      "Check saved pagination, page overlap and changing query totals. Provider totals are snapshots, not a list of missing URLs.",
      "ROBOTA_PAGING_OR_TOTAL");
    if(gap.Signals.Contains("RSS_EMPTY_UNVERIFIED"))
     Add("P1","EMPTY_FEED_UNVERIFIED",provider,query,
      "Check saved RSS response and parser with fixtures; a zero-item feed is not verified zero jobs.",
      "RSS_EMPTY_UNVERIFIED");
    if(gap.MatchesStoredDiscoveries &&
       gap.NewToEarlierQueries==0 && gap.ObservedDistinctUrls>0 &&
       (query.StartsWith("dou-rss-",StringComparison.OrdinalIgnoreCase) ||
        query.StartsWith("djinni-rss-",StringComparison.OrdinalIgnoreCase) ||
        query=="wwr-programming"))
     Add("P2","QUERY_ADDS_NO_MARGINAL_URLS",provider,query,
      "Compare this query's saved overlap across several clean runs before changing keywords. One run does not justify disabling a query.",
      "ZERO_MARGINAL_WITH_OVERLAP");
   }
   if(item.Signals.Contains("OBSERVED_REFERENCES_DOWN_OVER_50_PERCENT"))
    Add("P1","OBSERVED_DISCOVERY_DROP",provider,query,
     "Compare provider query, filters and historical reports; an observed count drop is not proof of connector loss.",
     "OBSERVED_REFERENCES_DOWN_OVER_50_PERCENT");
   if(item.Signals.Contains("LAST_OBSERVATION_OVER_48H"))
    Add("P2","STALE_QUERY_OBSERVATION",provider,query,
     "Inspect collector cadence and configured source selection; do not treat stale discovery as employer closure.",
     "LAST_OBSERVATION_OVER_48H");
   if(item.Signals.Contains("FULL_TEXT_ENDPOINT_PAUSED"))
    Add("P2","ROBOTA_DETAIL_RESTRICTED",provider,query,
     "Keep the HTTP 403 full-text endpoint paused; inspect stored previews and permission-compliant alternatives manually.",
     "FULL_TEXT_ENDPOINT_PAUSED");
   else if(item.Signals.Contains("DETAIL_FETCH_FAILURES"))
    Add("P2","DETAIL_EVIDENCE_INCOMPLETE",provider,query,
     "Review failed detail evidence and source permissions before any bounded manual recheck.",
     "DETAIL_FETCH_FAILURES");
  }

  // The vacancy table stores canonical sources, not which query first found
  // a job. Attribute these only at provider level, never to a single query.
  foreach(var source in funnel.Sources)
  {
   var provider=source.Source.ToLowerInvariant();
   if(provider is not ("dou" or "djinni" or "robota" or "wwr"))continue;
   if(source.ReviewTargetBackend+source.ReviewPartialDotNet>0 &&
      source.StatusUnverified>0)
    Add("P2","REVIEW_TARGET_VACANCIES",provider,null,
     "Review stored high-priority .NET Backend vacancies and independently verify employer status and Ukraine eligibility before applying. Counts may refer to different jobs.",
     "TARGET_REVIEW_PRESENT","STATUS_UNVERIFIED_PRESENT");
   if(source.PreviewOnly>0)
    Add("P2","REVIEW_PREVIEW_ONLY",provider,null,
     "Assess preview-only vacancies against direct authorized source pages manually; never retry paused 403 details or infer exclusion from a short excerpt.",
     "PREVIEW_ONLY_PRESENT");
  }

  if(funnel.OmittedByLimit>0)
   Add("P1","TRUNCATED_VACANCY_AUDIT","ALL",null,
    "Increase the configured bounded local export limit if needed; sample counts cannot establish complete database totals.",
    "OMITTED_VACANCIES");

  return new CoverageActionPlanReport(now,coverage.NewestReportAt,
   coverage.ReportsExamined,funnel.Evaluated,funnel.TotalStored,
   funnel.OmittedByLimit,funnel.OmittedByLimit==0,
   actions.OrderBy(x=>x.Priority,StringComparer.Ordinal)
    .ThenBy(x=>x.Provider,StringComparer.OrdinalIgnoreCase)
    .ThenBy(x=>x.Query,StringComparer.OrdinalIgnoreCase)
    .ThenBy(x=>x.Code,StringComparer.Ordinal).ToArray(),
   "Read-only, evidence-based manual investigation plan. Actions neither execute "+
   "source requests nor change configuration, vacancies or applications. "+
   "Capped feeds and stored crawl results cannot prove market completeness. "+
   "Per-provider vacancy counts are not per-query attribution, and no "+
   "recommendation proves that an employer is accepting remote applicants from Ukraine.");
 }
}
