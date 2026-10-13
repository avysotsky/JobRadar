namespace JobRadar;

/// <summary>
/// Operator-facing recommendation safety gates, distinct from technical fit.
/// A vacancy stays visible in the full audit even if it cannot be recommended.
/// </summary>
public static class VacancyRecommendationGates
{
 public static VacancyAssessment Apply(
  VacancyAssessment assessment,DateTimeOffset now,
  IReadOnlyDictionary<string,EmploymentDecision>? decisions=null)
 {
  ArgumentNullException.ThrowIfNull(assessment);
  var job=assessment.Vacancy;
  var warnings=assessment.Warnings.ToList();
  var bucket=assessment.Bucket;
  if(decisions is not null &&
    decisions.TryGetValue(Storage.CanonicalEmploymentUrl(job.Url),out var decision))
  {
   if(decision.Decision=="Applied")
   {
    bucket=FitBucket.Excluded;
    warnings.Add("USER_RECORDED_APPLIED: резюме уже отправлено; повторный отклик не рекомендовать");
   }
   else if(decision.Decision=="Inactive")
   {
    bucket=FitBucket.Excluded;
    warnings.Add("USER_RECORDED_INACTIVE: пользователь сообщил, что вакансия неактивна");
   }
  }

  // LastSeen reflects the last successful ingestion, not a provider-confirmed
  // offer. Staleness can indicate capped RSS or incomplete scans, NOT closure.
  // Never infer that a stale job is definitively closed.
  if(now-job.LastSeen>TimeSpan.FromHours(24))
  {
   warnings.Add("LAST_SEEN_OVER_24H: не обнаружена за последние 24 часа; актуальность проверить");
   if(bucket==FitBucket.LikelyFit)bucket=FitBucket.NeedsReview;
  }

  // A score and an HTTP 200 are not evidence that applications are accepted.
  // Only independently recorded positive provider status allows the strong
  // recommendation export. Unknown stays available for manual review.
  if(bucket==FitBucket.LikelyFit && job.IsOpen!=true)
  {
   bucket=FitBucket.NeedsReview;
   warnings.Add("OPEN_NOT_VERIFIED: нет подтверждения, что вакансия принимает отклики");
  }
  return assessment with {Bucket=bucket,Warnings=warnings};
 }
}
