namespace JobRadar;

/// <summary>
/// Small, local-only list of vacancies first RECORDED recently. A stored
/// positive status is evidence at its timestamp, never proof of current
/// employer availability or permission to work remotely from Ukraine.
/// </summary>
public sealed record FreshShortlistItem(
 string Source,string Url,string Title,string? Company,
 DateTimeOffset FirstRecordedAt,DateTimeOffset LastSeen,
 DateTimeOffset? PublishedAt,DateTimeOffset? StatusCheckedAt,
 string StatusEvidenceLevel,string DiscoveryRecency,
 FitBucket Bucket,VacancyReviewTier? ReviewTier,
 WorkMode WorkMode,int Score,bool HasFullText,
 IReadOnlyList<string> Reasons,IReadOnlyList<string> Warnings);

public sealed record FreshShortlistReport(
 DateTimeOffset EvaluatedAt,DateTimeOffset FirstRecordedSince,
 int Evaluated,long TotalStored,long OmittedBySnapshotLimit,
 int RecentRecordedJobs,int EligibleForReview,int Returned,
 IReadOnlyList<FreshShortlistItem> Vacancies,string Interpretation);

public static class FreshShortlist
{
 public const int LookbackDays=7;
 public const int MaxItems=20;

 public static FreshShortlistReport Analyze(
  IReadOnlyList<VacancySnapshot> vacancies,
  IReadOnlyDictionary<(string Source,string Url),DateTimeOffset> firstRecorded,
  IReadOnlyDictionary<string,EmploymentDecision> decisions,
  long totalStored,DateTimeOffset now)
 {
  ArgumentNullException.ThrowIfNull(vacancies);
  ArgumentNullException.ThrowIfNull(firstRecorded);
  ArgumentNullException.ThrowIfNull(decisions);
  if(totalStored<vacancies.Count || totalStored<0)
   throw new ArgumentOutOfRangeException(nameof(totalStored));
  var since=now.AddDays(-LookbackDays);
  var results=new List<FreshShortlistItem>();
  int recent=0;
  foreach(var job in vacancies)
  {
   if(!firstRecorded.TryGetValue((job.Source,job.Url),out var first) ||
      first<since || first>now)
    continue;
   recent++;
   if(!IsOriginalTrustedLink(job.Source,job.Url))continue;
   var assessed=VacancyRecommendationGates.Apply(
    VacancyTriage.Assess(job),now,decisions);
   if(assessed.Bucket==FitBucket.Excluded ||
      assessed.WorkMode is WorkMode.Hybrid or WorkMode.Onsite)
    continue;
   VacancyReviewTier? tier=null;
   if(assessed.Bucket==FitBucket.NeedsReview)
   {
    tier=VacancyReviewPriority.Assess(assessed).Tier;
    if(tier is not (VacancyReviewTier.TargetDotNetBackend or
       VacancyReviewTier.PartialDotNetEvidence))
     continue;
   }
   var status=StatusLevel(job,now);
   var recency=job.LastSeen>now?"FUTURE_LAST_SEEN_UNVERIFIED":
    now-job.LastSeen<=TimeSpan.FromHours(24)
     ?"DISCOVERED_WITHIN_24H":"LAST_SEEN_OVER_24H";
   results.Add(new FreshShortlistItem(job.Source,job.Url,job.Title,job.Company,
    first,job.LastSeen,job.PublishedAt,job.StatusCheckedAt,
    status,recency,assessed.Bucket,tier,
    assessed.WorkMode,assessed.Score,job.HasFullText,
    assessed.Reasons,assessed.Warnings));
  }
  var sorted=results.OrderBy(x=>x.Bucket==FitBucket.LikelyFit?0:
    x.ReviewTier==VacancyReviewTier.TargetDotNetBackend?1:2)
   .ThenBy(x=>x.DiscoveryRecency=="DISCOVERED_WITHIN_24H"?0:1)
   .ThenBy(x=>x.WorkMode==WorkMode.Remote?0:1)
   .ThenByDescending(x=>x.Score)
   .ThenByDescending(x=>x.FirstRecordedAt)
   .ThenBy(x=>x.Source,StringComparer.Ordinal)
   .ThenBy(x=>x.Url,StringComparer.Ordinal).ToArray();
  return new FreshShortlistReport(now,since,vacancies.Count,totalStored,
   totalStored-vacancies.Count,recent,sorted.Length,
   Math.Min(MaxItems,sorted.Length),sorted.Take(MaxItems).ToArray(),
   "FirstRecordedAt is the earliest AVAILABLE local job_discoveries event "+
   "(history began in Phase 15), not the employer's posting date. "+
   "StatusEvidenceLevel is timestamped stored evidence, never live verification "+
   "of current applications. Unknown opening and Ukraine eligibility "+
   "remain review tasks. RemoteOnly is not inferred from job-board branding. "+
   "Applied/Inactive, definite Senior-only/office/hybrid, restricted-location "+
   "and difficult-requirement listings are not recommended. "+
   "An omitted DB snapshot may hide additional candidates; output "+
   "has a hard 20-item limit and never makes network requests.");
 }

 private static string StatusLevel(VacancySnapshot job,DateTimeOffset now)
 {
  if(job.IsOpen==true && job.StatusCheckedAt is { } stamp &&
     !string.IsNullOrWhiteSpace(job.StatusEvidence))
  {
   return stamp<=now && now-stamp<=TimeSpan.FromHours(24)
    ?"POSITIVE_EVIDENCE_RECORDED_24H":"POSITIVE_EVIDENCE_STALE";
  }
  if(job.IsOpen==false)return "FALSE_STATUS_WITHOUT_VERIFIED_CLOSURE";
  return "OPEN_UNVERIFIED";
 }

 public static bool IsOriginalTrustedLink(string source,string? url)
 {
  if(!Storage.IsAcceptableEmploymentUrl(url))return false;
  var uri=new Uri(url!);
  if(uri.Port!=443)return false;
  return source switch
  {
   "dou"=>uri.Host.Equals("jobs.dou.ua",StringComparison.OrdinalIgnoreCase),
   "djinni"=>uri.Host.Equals("djinni.co",StringComparison.OrdinalIgnoreCase)
    || uri.Host.Equals("www.djinni.co",StringComparison.OrdinalIgnoreCase),
   "robota"=>uri.Host.Equals("robota.ua",StringComparison.OrdinalIgnoreCase)
    || uri.Host.Equals("www.robota.ua",StringComparison.OrdinalIgnoreCase),
   "wwr"=>uri.Host.Equals("weworkremotely.com",StringComparison.OrdinalIgnoreCase)
    || uri.Host.Equals("www.weworkremotely.com",StringComparison.OrdinalIgnoreCase),
   _=>false
  };
 }
}
