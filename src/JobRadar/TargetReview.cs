namespace JobRadar;

/// <summary>
/// A bounded, offline operator queue: target evidence outranks a raw score.
/// No result here asserts provider-current opening or Ukraine eligibility.
/// </summary>
public sealed record TargetReviewItem(
 string Source,string Url,string Title,string? Company,
 int Score,WorkMode WorkMode,VacancyReviewTier Tier,string TierReason,
 bool HasFullText,DateTimeOffset LastSeen,bool? IsOpen,
 string OpeningEvidence,IReadOnlyList<string> Reasons,
 IReadOnlyList<string> Warnings);

public sealed record TargetReviewReport(
 DateTimeOffset EvaluatedAt,int Evaluated,long TotalStored,
 long OmittedBySnapshotLimit,int TargetBackendCandidates,
 int PartialDotNetCandidates,int Returned,
 IReadOnlyList<TargetReviewItem> Vacancies,string Interpretation);

public static class TargetReview
{
 public const int MaxItems=30;

 public static TargetReviewReport Analyze(
  IReadOnlyList<VacancySnapshot> vacancies,
  IReadOnlyDictionary<string,EmploymentDecision> decisions,
  long totalStored,DateTimeOffset now)
 {
  ArgumentNullException.ThrowIfNull(vacancies);
  ArgumentNullException.ThrowIfNull(decisions);
  if(totalStored<vacancies.Count || totalStored<0)
   throw new ArgumentOutOfRangeException(nameof(totalStored));
  var list=new List<TargetReviewItem>();
  foreach(var vacancy in vacancies)
  {
   var item=VacancyRecommendationGates.Apply(
    VacancyTriage.Assess(vacancy),now,decisions);
   if(item.Bucket==FitBucket.Excluded ||
      item.WorkMode is WorkMode.Hybrid or WorkMode.Onsite ||
      !Storage.IsAcceptableEmploymentUrl(vacancy.Url))
    continue;
   var priority=VacancyReviewPriority.Assess(item);
   if(priority.Tier is not (VacancyReviewTier.TargetDotNetBackend or
      VacancyReviewTier.PartialDotNetEvidence))
    continue;
   var evidence=vacancy.IsOpen==true &&
    vacancy.StatusCheckedAt is not null &&
    !string.IsNullOrWhiteSpace(vacancy.StatusEvidence)
     ? "HISTORICAL_POSITIVE_STATUS":"OPEN_UNVERIFIED";
   list.Add(new TargetReviewItem(
    vacancy.Source,vacancy.Url,vacancy.Title,vacancy.Company,
    item.Score,item.WorkMode,priority.Tier,priority.Reason,
    vacancy.HasFullText,vacancy.LastSeen,vacancy.IsOpen,
    evidence,item.Reasons,item.Warnings));
  }
  var ordered=list.OrderBy(x=>x.Tier)
   .ThenBy(x=>x.WorkMode==WorkMode.Remote?0:1)
   .ThenBy(x=>x.HasFullText?0:1)
   .ThenByDescending(x=>x.LastSeen)
   .ThenByDescending(x=>x.Score)
   .ThenBy(x=>x.Source,StringComparer.Ordinal)
   .ThenBy(x=>x.Url,StringComparer.Ordinal).ToArray();
  return new TargetReviewReport(now,vacancies.Count,totalStored,
   totalStored-vacancies.Count,
   ordered.Count(x=>x.Tier==VacancyReviewTier.TargetDotNetBackend),
   ordered.Count(x=>x.Tier==VacancyReviewTier.PartialDotNetEvidence),
   Math.Min(ordered.Length,MaxItems),ordered.Take(MaxItems).ToArray(),
   "Targets are selected by role/requirements review tier, NOT by raw score. "+
   "Previously Applied or Inactive URLs are suppressed. No provider HTTP "+
   "calls, employer-status refresh, or applications are performed. "+
   "OPEN_UNVERIFIED means the job must be checked on its original site. "+
   "LastSeen is local collection time, not employer posting time. "+
   "Fullstack, mandatory high English, country restrictions, senior-only, "+
   "office and hybrid requirements are not presented as target jobs. "+
   "Snapshot truncation can omit additional matches; output is capped at 30.");
 }
}
