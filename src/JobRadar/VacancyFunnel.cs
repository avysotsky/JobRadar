namespace JobRadar;

/// <summary>
/// Evidence counts over a bounded snapshot of stored vacancies. No number in
/// this report establishes whole-market coverage or remote work eligibility.
/// </summary>
public sealed record VacancyFunnelRow(
 string Source,int Stored,int FullText,int PreviewOnly,int SeenWithin24Hours,
 int PositiveStatusEvidenceRecorded,int ExplicitProviderClosed,int StatusUnverified,
 int RemoteTextEvidence,int GeographicRestriction,
 int EnglishB2PlusMentioned,int OwnerApplied,int OwnerInactive,
 int LikelyFit,int NeedsReview,int Excluded,
 int ReviewTargetBackend,int ReviewPartialDotNet,int ReviewDifficultRequirements,
 int ReviewOtherSpecialization);

public sealed record VacancyFunnelReport(
 DateTimeOffset EvaluatedAt,int Evaluated,long TotalStored,long OmittedByLimit,
 VacancyFunnelRow Totals,IReadOnlyList<VacancyFunnelRow> Sources,
 string Interpretation);

public static class VacancyFunnel
{
 private sealed record Entry(
  string Source,VacancyAssessment Assessment,bool FullText,
  bool Recent,bool Positive,bool Closed,bool Remote,bool Restricted,
  bool B2Mentioned,bool Applied,bool Inactive,VacancyReviewTier? Tier);

 public static VacancyFunnelReport Analyze(
  IReadOnlyList<VacancySnapshot> vacancies,
  IReadOnlyDictionary<string,EmploymentDecision> decisions,
  long totalStored,DateTimeOffset now)
 {
  ArgumentNullException.ThrowIfNull(vacancies);
  ArgumentNullException.ThrowIfNull(decisions);
  if(totalStored<0)throw new ArgumentOutOfRangeException(nameof(totalStored));

  var rows=new List<Entry>(vacancies.Count);
  foreach(var job in vacancies)
  {
   // User decisions are separate from provider evidence. Do not mark an
   // Applied/Inactive record as provider-closed or omit it from the audit.
   EmploymentDecision? own=null;
   if(Storage.IsAcceptableEmploymentUrl(job.Url))
    decisions.TryGetValue(Storage.CanonicalEmploymentUrl(job.Url),out own);

   var assessment=VacancyRecommendationGates.Apply(
    VacancyTriage.Assess(job),now,decisions);
   var text=job.Description.Length>0?job.Description:job.Preview??"";
   var restricted=VacancyEligibility.Analyze(job.Title,text).LocationRestricted;
   var closed=job.IsOpen==false && job.StatusEvidence is
    ("html:explicit-closed-banner" or "html:standalone-closed-message" or
     "html:explicit-closed-heading" or "html:explicit-closed-title");
   // A historical positive observation is not necessarily a current opening.
   // Match the gate's positive-evidence prerequisites, NOT a fresh-open claim.
   var positive=job.IsOpen==true && job.StatusCheckedAt.HasValue &&
    !string.IsNullOrWhiteSpace(job.StatusEvidence);
   var age=now-job.LastSeen;
   rows.Add(new Entry(job.Source,assessment,job.HasFullText,
    age>=TimeSpan.Zero && age<=TimeSpan.FromHours(24),
    positive,closed,assessment.WorkMode==WorkMode.Remote,restricted,
    assessment.Warnings.Any(x=>x.Contains("B2+",StringComparison.Ordinal)),
    own?.Decision=="Applied",own?.Decision=="Inactive",
    assessment.Bucket==FitBucket.NeedsReview
     ?VacancyReviewPriority.Assess(assessment).Tier:null));
  }

  static VacancyFunnelRow Count(string source,IEnumerable<Entry> values)
  {
   var list=values.ToArray();
   int N(Func<Entry,bool> predicate)=>list.Count(predicate);
   return new VacancyFunnelRow(source,list.Length,
    N(x=>x.FullText),N(x=>!x.FullText),N(x=>x.Recent),
    N(x=>x.Positive),N(x=>x.Closed),N(x=>!x.Positive&&!x.Closed),
    N(x=>x.Remote),N(x=>x.Restricted),N(x=>x.B2Mentioned),
    N(x=>x.Applied),N(x=>x.Inactive),
    N(x=>x.Assessment.Bucket==FitBucket.LikelyFit),
    N(x=>x.Assessment.Bucket==FitBucket.NeedsReview),
    N(x=>x.Assessment.Bucket==FitBucket.Excluded),
    N(x=>x.Tier==VacancyReviewTier.TargetDotNetBackend),
    N(x=>x.Tier==VacancyReviewTier.PartialDotNetEvidence),
    N(x=>x.Tier==VacancyReviewTier.DifficultRequirements),
    N(x=>x.Tier==VacancyReviewTier.OtherSpecialization));
  }

  var sources=rows.GroupBy(x=>x.Source,StringComparer.OrdinalIgnoreCase)
   .OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)
   .Select(x=>Count(x.Key,x)).ToArray();
  return new VacancyFunnelReport(now,rows.Count,totalStored,
   Math.Max(0L,totalStored-rows.Count),Count("ALL",rows),sources,
   "Offline bounded stored-vacancy audit only. PositiveStatusEvidenceRecorded "+
   "may be historical. RemoteTextEvidence and no detected geographic restriction "+
   "do not prove Ukraine eligibility. LikelyFit requires recommendation gates; "+
   "NeedsReview includes unverified openings. Applied/Inactive are user decisions, "+
   "not provider closure. OmittedByLimit means this is not a complete DB audit.");
 }
}
