using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class VacancyReviewPriorityTests
{
 private static readonly DateTimeOffset Now=new(2026,10,13,12,0,0,TimeSpan.Zero);
 private static VacancyAssessment Evaluate(string title,string description,bool full=true)
 {
  var snapshot=new VacancySnapshot("dou","https://jobs.dou.ua/vacancies/1",
   title,null,null,description,full,null,Now,Now,["dou"]);
  var preliminary=VacancyTriage.Assess(snapshot);
  return VacancyRecommendationGates.Apply(preliminary,Now);
 }

 [Theory]
 [InlineData("Middle .NET Backend","Fully remote ASP.NET Core REST API PostgreSQL EF Core",VacancyReviewTier.TargetDotNetBackend)]
 [InlineData("Junior C# API Developer","Fully remote ASP.NET Core",VacancyReviewTier.TargetDotNetBackend)]
 [InlineData("Middle C# Developer","Fully remote development of internal tools",VacancyReviewTier.PartialDotNetEvidence)]
 [InlineData("Middle/Senior .NET Backend","Fully remote ASP.NET Core REST API",VacancyReviewTier.DifficultRequirements)]
 [InlineData("Middle .NET Backend","Fully remote ASP.NET Core REST API, English C1 required",VacancyReviewTier.DifficultRequirements)]
 [InlineData("Middle .NET Backend","Hybrid: 2 days in office. ASP.NET Core REST API",VacancyReviewTier.DifficultRequirements)]
 [InlineData("Middle .NET WPF Developer","Fully remote ASP.NET Core REST API",VacancyReviewTier.OtherSpecialization)]
 [InlineData("Middle FullStack .NET Developer","Fully remote REST API React",VacancyReviewTier.OtherSpecialization)]
 [InlineData("Middle Java Backend","Fully remote Spring REST API",VacancyReviewTier.OtherSpecialization)]
 public void ReviewTierReflectsRealTargetNotJustTechnologyScore(
  string title,string body,VacancyReviewTier expected)
 {
  var candidate=Evaluate(title,body);
  Assert.Equal(expected,VacancyReviewPriority.Assess(candidate).Tier);
 }

 [Fact]
 public void ReviewTierCannotClaimProviderOpenOrOverwriteExistingFitBucket()
 {
  var candidate=Evaluate("Middle .NET Backend",
   "Fully remote ASP.NET Core REST API EF Core PostgreSQL");
  Assert.Equal(FitBucket.NeedsReview,candidate.Bucket);
  Assert.Null(candidate.Vacancy.IsOpen);
  var ranking=VacancyReviewPriority.Assess(candidate);
  Assert.Equal(VacancyReviewTier.TargetDotNetBackend,ranking.Tier);
  Assert.Equal(FitBucket.NeedsReview,candidate.Bucket);
 }

 [Fact]
 public void IncompleteRobotaPreviewWithDotNetBackendRemainsReviewable()
 {
  var candidate=Evaluate("Junior+/Middle .NET Backend",
   "ASP.NET Core REST API remote",full:false);
  Assert.Equal(FitBucket.NeedsReview,candidate.Bucket);
  Assert.Equal(VacancyReviewTier.TargetDotNetBackend,
   VacancyReviewPriority.Assess(candidate).Tier);
 }
}
