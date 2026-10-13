using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class VacancyRecommendationGateTests
{
 private static readonly DateTimeOffset Now=new(2026,10,13,10,0,0,TimeSpan.Zero);
 private static VacancyAssessment Suitable(bool? isOpen,DateTimeOffset seen,
  string url="https://jobs.dou.ua/vacancies/123/")=>
  VacancyTriage.Assess(new VacancySnapshot("dou",url,
   "Middle .NET Backend",null,null,
   "Fully remote ASP.NET Core Web API REST API EF Core PostgreSQL RabbitMQ",
   true,isOpen,Now.AddDays(-1),seen,["dou"],
   isOpen==true?Now:null,isOpen==true?"test:affirmative-open":null));

 [Fact]
 public void OnlyFreshVerifiedOpenCandidateCanEnterActionableShortlist()
 {
  var confirmed=VacancyRecommendationGates.Apply(Suitable(true,Now.AddHours(-2)),Now);
  Assert.Equal(FitBucket.LikelyFit,confirmed.Bucket);
  var unknown=VacancyRecommendationGates.Apply(Suitable(null,Now.AddHours(-2)),Now);
  Assert.Equal(FitBucket.NeedsReview,unknown.Bucket);
  Assert.Contains(unknown.Warnings,x=>x.StartsWith("OPEN_NOT_VERIFIED:"));
  var stale=VacancyRecommendationGates.Apply(Suitable(true,Now.AddHours(-25)),Now);
  Assert.Equal(FitBucket.NeedsReview,stale.Bucket);
  Assert.Contains(stale.Warnings,x=>x.StartsWith("LAST_SEEN_OVER_24H:"));
  var exact=VacancyRecommendationGates.Apply(Suitable(true,Now.AddHours(-24)),Now);
  Assert.Equal(FitBucket.LikelyFit,exact.Bucket);
 }

 [Fact]
 public void OwnerDecisionOverridesTechnicalFitButCannotClaimProviderClosure()
 {
  var url="https://jobs.dou.ua/vacancies/123/";
  var decisions=new Dictionary<string,EmploymentDecision>(StringComparer.OrdinalIgnoreCase)
  {
   [Storage.CanonicalEmploymentUrl(url)]=new(url,"Applied",Now)
  };
  var result=VacancyRecommendationGates.Apply(Suitable(true,Now,url),Now,decisions);
  Assert.Equal(FitBucket.Excluded,result.Bucket);
  Assert.Contains(result.Warnings,x=>x.StartsWith("USER_RECORDED_APPLIED:"));
  decisions[Storage.CanonicalEmploymentUrl(url)]=new(url,"Inactive",Now);
  var inactive=VacancyRecommendationGates.Apply(Suitable(null,Now,url),Now,decisions);
  Assert.Equal(FitBucket.Excluded,inactive.Bucket);
  Assert.Contains(inactive.Warnings,x=>x.StartsWith("USER_RECORDED_INACTIVE:"));
  Assert.Null(inactive.Vacancy.IsOpen);
 }

 [Fact]
 public void MalformedSavedUrlIsNeverDiscardedByDecisionLookup()
 {
  var record=Suitable(null,Now,"legacy-malformed-uri");
  var safe=VacancyRecommendationGates.Apply(record,Now,
   new Dictionary<string,EmploymentDecision>());
  Assert.Equal(FitBucket.NeedsReview,safe.Bucket);
 }

 [Theory]
 [InlineData("https://jobs.dou.ua/vacancies/123/","https://jobs.dou.ua/vacancies/123")]
 [InlineData("https://jobs.dou.ua/vacancies/123?ref=one","https://jobs.dou.ua/vacancies/123?ref=one")]
 public void CanonicalIdentityPreservesMeaningfulQueryButIgnoresTrailingSlash(
  string raw,string expected)
 {
  Assert.Equal(expected,Storage.CanonicalEmploymentUrl(raw));
 }

 [Theory]
 [InlineData("http://jobs.dou.ua/vacancies/1")]
 [InlineData("https://user:secret@jobs.dou.ua/vacancies/1")]
 [InlineData("https://jobs.dou.ua/vacancies/1#fragment")]
 [InlineData("https://jobs.dou.ua/")]
 [InlineData("not an url")]
 public void InvalidEmploymentDecisionUrlsFailClosed(string raw)
 {
  Assert.False(Storage.IsAcceptableEmploymentUrl(raw));
 }
}
