namespace JobRadar;

/// <summary>
/// Retrospective triage for uniquely attributed query discoveries in one
/// already stored crawl. Not a statement about employer status at crawl time.
/// </summary>
public sealed record QueryActionableYieldRow(
 string Query,string Provider,int ObservedUrls,int MatchedCurrentSnapshots,
 int? MarginalUrls,int? CurrentLikelyFit,int? CurrentTargetBackendReview,
 int? CurrentPartialDotNetReview,int? CurrentOtherReview,int? CurrentExcluded,
 int? CurrentStatusUnknown,IReadOnlyList<string> Signals);

public sealed record QueryActionableYieldReport(
 DateTimeOffset CrawlStartedAt,DateTimeOffset CrawlEndedAt,DateTimeOffset EvaluatedAt,
 long CurrentDatabaseJobs,int CurrentSnapshotJobs,long CurrentSnapshotOmitted,
 IReadOnlyList<QueryActionableYieldRow> Queries,
 IReadOnlyDictionary<string,int?> CurrentTargetBackendMarginalByProvider,
 string Interpretation);

/// <summary>
/// Phase 36: pair verified per-query discovery events with current persisted
/// vacancy assessments without attributing unrelated jobs or inventing
/// missing feed entries. No HTTP or writes.
/// </summary>
public static class QueryActionableYield
{
 public static QueryActionableYieldReport Analyze(
  CrawlReport run,DiscoveryGapReport gaps,
  IReadOnlyList<DiscoveryGapObservation> observations,
  IReadOnlyList<VacancySnapshot> currentVacancies,
  IReadOnlyDictionary<string,EmploymentDecision> ownDecisions,
  long currentTotal,DateTimeOffset now)
 {
  ArgumentNullException.ThrowIfNull(run);
  ArgumentNullException.ThrowIfNull(gaps);
  ArgumentNullException.ThrowIfNull(observations);
  ArgumentNullException.ThrowIfNull(currentVacancies);
  ArgumentNullException.ThrowIfNull(ownDecisions);
  if(currentTotal<currentVacancies.Count || currentTotal<0)
   throw new ArgumentOutOfRangeException(nameof(currentTotal));
  if(run.StartedAt!=gaps.StartedAt || run.EndedAt!=gaps.EndedAt ||
     run.Sources is null || gaps.Queries is null)
   throw new InvalidDataException("Discovery audit does not describe the supplied crawl");
  var keys=run.Sources.Where(x=>DiscoveryGapAudit.ProviderForQuery(x.Source)!=null)
   .Select(x=>x.Source).ToArray();
  if(keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=keys.Length)
   throw new InvalidDataException("Duplicate query identities in crawl report");
  var gapRows=gaps.Queries.ToDictionary(x=>x.Query,StringComparer.OrdinalIgnoreCase);
  var eventSets=observations.Where(x=>keys.Contains(x.Query,StringComparer.OrdinalIgnoreCase) &&
      string.Equals(x.Provider,DiscoveryGapAudit.ProviderForQuery(x.Query),
       StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Url))
   .GroupBy(x=>x.Query,StringComparer.OrdinalIgnoreCase)
   .ToDictionary(x=>x.Key,x=>x.Select(y=>y.Url).ToHashSet(StringComparer.OrdinalIgnoreCase),
    StringComparer.OrdinalIgnoreCase);

  // Canonical saved vacancy identities are source + URL. Never match by
  // title or infer query attribution from all-time job_queries mappings.
  var snapshots=new Dictionary<string,VacancySnapshot>(StringComparer.OrdinalIgnoreCase);
  foreach(var vacancy in currentVacancies)
  {
   if(DiscoveryGapAudit.ProviderForQuery(vacancy.Source)==null &&
      vacancy.Source is not ("dou" or "djinni" or "robota" or "wwr"))
    continue;
   var key=vacancy.Source+"\u001f"+vacancy.Url;
   if(!snapshots.TryAdd(key,vacancy))
    throw new InvalidDataException("Duplicate canonical vacancy in current snapshot");
  }

  var seen=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
  var compromised=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var rows=new List<QueryActionableYieldRow>();
  foreach(var query in keys)
  {
   var provider=DiscoveryGapAudit.ProviderForQuery(query)!;
   if(!seen.TryGetValue(provider,out var preceding))
   {
    preceding=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    seen.Add(provider,preceding);
   }
   if(!eventSets.TryGetValue(query,out var urls))
    urls=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   gapRows.TryGetValue(query,out var gap);
   var newUrls=urls.Where(preceding.Add).ToArray();
   var found=urls.Count(x=>snapshots.ContainsKey(provider+"\u001f"+x));
   var signals=new SortedSet<string>(StringComparer.Ordinal);
   bool countsVerified=gap is not null && gap.MatchesStoredDiscoveries &&
     gap.NewToEarlierQueries.HasValue && gap.ObservedDistinctUrls==urls.Count &&
     gap.NewToEarlierQueries.Value==newUrls.Length;
   if(!countsVerified)
   {
    compromised.Add(provider);
    signals.Add("RUN_QUERY_ATTRIBUTION_UNVERIFIED");
   }
   bool complete=countsVerified && !compromised.Contains(provider);
   var unmatched=newUrls.Count(x=>!snapshots.ContainsKey(provider+"\u001f"+x));
   if(unmatched>0)
   {
    signals.Add("CURRENT_VACANCY_SNAPSHOT_MISSING");
    // With missing current jobs, neither the historical count nor any
    // current fit subtotal may be treated as a complete measurement.
    complete=false;
   }
   if(currentTotal>currentVacancies.Count)
    signals.Add("CURRENT_SNAPSHOT_TRUNCATED");
   signals.Add("CURRENT_FIT_NOT_AT_DISCOVERY_TIME");
   signals.Add("PROVIDER_WIDE_RECALL_UNKNOWN");

   int likely=0,target=0,partial=0,other=0,excluded=0,unknown=0;
   if(complete)
   {
    foreach(var url in newUrls)
    {
     var snapshot=snapshots[provider+"\u001f"+url];
     var assessment=VacancyRecommendationGates.Apply(
      VacancyTriage.Assess(snapshot),now,ownDecisions);
     if(snapshot.IsOpen is null || snapshot.StatusCheckedAt is null ||
        string.IsNullOrWhiteSpace(snapshot.StatusEvidence))
      unknown++;
     switch(assessment.Bucket)
     {
      case FitBucket.LikelyFit: likely++;break;
      case FitBucket.Excluded: excluded++;break;
      case FitBucket.NeedsReview:
       var tier=VacancyReviewPriority.Assess(assessment).Tier;
       if(tier==VacancyReviewTier.TargetDotNetBackend)target++;
       else if(tier==VacancyReviewTier.PartialDotNetEvidence)partial++;
       else other++;
       break;
     }
    }
   }
   rows.Add(new QueryActionableYieldRow(query,provider,urls.Count,found,
    complete?newUrls.Length:null,complete?likely:null,complete?target:null,
    complete?partial:null,complete?other:null,complete?excluded:null,
    complete?unknown:null,signals.ToArray()));
  }
  var totals=rows.GroupBy(x=>x.Provider,StringComparer.OrdinalIgnoreCase)
   .ToDictionary(x=>x.Key,x=>x.All(r=>r.CurrentTargetBackendReview.HasValue)
     ?(int?)x.Sum(r=>r.CurrentTargetBackendReview!.Value):null,
    StringComparer.OrdinalIgnoreCase);
  return new QueryActionableYieldReport(run.StartedAt,run.EndedAt,now,
   currentTotal,currentVacancies.Count,
   currentTotal-currentVacancies.Count,rows,totals,
   "MarginalUrls are unique to earlier queries of the SAME provider in the "+
   "saved crawl order, not a provider-wide recall percentage. Fit is based "+
   "on TODAY'S stored snapshot/owner decisions, not the state at discovery. "+
   "Unknown opening status, RSS caps and Ukraine location eligibility "+
   "require independent checking. Missing snapshots or unverified upstream "+
   "queries produce null metrics rather than false zero. No automatic query "+
   "configuration, crawling or job applications are performed.");
 }
}
