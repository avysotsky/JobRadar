using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class VacancyTriageTests
{
 private static VacancySnapshot Job(string title,string description,
  bool full=true,bool? open=null)=>
  new("dou","https://jobs.dou.ua/vacancies/42/",title,"Example Ltd",
    "C# backend",description,full,open,DateTimeOffset.UtcNow,
    DateTimeOffset.UtcNow,["dou-rss-C#"]);

 [Fact]
 public void FullRemoteMiddleDotNetWithRelevantStackIsLikelyFit()
 {
  var job=Job("Middle .NET Backend Developer",
   "Fully remote position in Ukraine. ASP.NET Core Web API, PostgreSQL, EF Core, RabbitMQ. "+
   "Build and maintain REST services and work in a distributed backend team.");
  var result=VacancyTriage.Assess(job);
  Assert.Equal(WorkMode.Remote,result.WorkMode);
  Assert.Equal(FitBucket.LikelyFit,result.Bucket);
  Assert.True(result.Score>=65);
  Assert.Contains(result.Reasons,r=>r.Contains("PostgreSQL"));
  Assert.Contains(result.Warnings,r=>r.Contains("Актуальность"));
 }

 [Theory]
 [InlineData("Middle .NET Backend", "Remote possible; 2 дні в офісі у Києві", WorkMode.Hybrid)]
 [InlineData("Middle .NET Backend", "Hybrid work format. ASP.NET Core", WorkMode.Hybrid)]
 [InlineData("Middle .NET Backend", "Fully remote, 2 days in office per week", WorkMode.Hybrid)]
 [InlineData("Middle .NET Backend", "Office only, no remote. ASP.NET Core", WorkMode.Onsite)]
 [InlineData("Middle .NET Backend", "We work with remote teams and remote access systems", WorkMode.Unknown)]
 public void WorkModeConflictsTakePriorityOverRemotePhrases(
  string title,string body,WorkMode expected)
 {
  Assert.Equal(expected,VacancyTriage.Assess(Job(title,body)).WorkMode);
 }

 [Theory]
 [InlineData("Senior .NET Engineer")]
 [InlineData("Lead C# Backend Developer")]
 [InlineData(".NET Software Architect")]
 public void SeniorOwnershipTitlesAreExcluded(string title)
 {
  var a=VacancyTriage.Assess(Job(title,"Fully remote. ASP.NET Core PostgreSQL EF Core REST API."));
  Assert.Equal(FitBucket.Excluded,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("Senior/Lead"));
 }

 [Fact]
 public void HybridCloudTechnologyDoesNotMeanHybridOffice()
 {
  var result=VacancyTriage.Assess(Job("Middle .NET Backend",
   "Fully remote position. ASP.NET Core REST API, hybrid cloud architecture, PostgreSQL and EF Core."));
  Assert.Equal(WorkMode.Remote,result.WorkMode);
  Assert.Equal(FitBucket.LikelyFit,result.Bucket);
 }

 [Fact]
 public void MixedMiddleSeniorRemainsReviewNotAutoExcludedOrLikely()
 {
  var a=VacancyTriage.Assess(Job("Middle / Senior .NET Backend",
   "Fully remote. ASP.NET Core, PostgreSQL, RabbitMQ and EF Core REST API."));
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("Смешанный"));
 }

 [Fact]
 public void B2EnglishAndSpokenEnglishRemainHumanReview()
 {
  var a=VacancyTriage.Assess(Job("Middle C# .NET Developer",
    "Fully remote. ASP.NET Core, REST API, PostgreSQL, EF Core. English B2 spoken required."));
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("B2+"));
  Assert.Contains(a.Warnings,w=>w.Contains("разговорный"));
 }

 [Fact]
 public void UnknownRemoteOrPreviewOnlyDoesNotBecomeConfirmed()
 {
  var unknown=VacancyTriage.Assess(Job("Middle .NET Backend","ASP.NET Core PostgreSQL EF Core"));
  Assert.Equal(FitBucket.NeedsReview,unknown.Bucket);
  Assert.Equal(WorkMode.Unknown,unknown.WorkMode);

  var preview=VacancyTriage.Assess(Job("Middle .NET Backend",
    "Fully remote ASP.NET Core PostgreSQL EF Core REST API",full:false));
  Assert.Equal(FitBucket.NeedsReview,preview.Bucket);
  Assert.Contains(preview.Warnings,w=>w.Contains("Полный текст"));
 }

 [Fact]
 public void ExplicitClosedVacancyIsExcludedButUnknownNotMarkedClosed()
 {
  var legacy=VacancyTriage.Assess(Job("Middle .NET Backend",
    "Fully remote ASP.NET Core PostgreSQL EF Core REST API",open:false));
  Assert.Equal(FitBucket.NeedsReview,legacy.Bucket);
  Assert.Contains(legacy.Warnings,w=>w.Contains("Исторический"));
  var confirmed=Job("Middle .NET Backend",
    "Fully remote ASP.NET Core PostgreSQL EF Core REST API",open:false)
    with {StatusEvidence="html:explicit-closed-banner",StatusCheckedAt=DateTimeOffset.UtcNow};
  var closed=VacancyTriage.Assess(confirmed);
  Assert.Equal(FitBucket.Excluded,closed.Bucket);
  var unknown=VacancyTriage.Assess(Job("Middle .NET Backend",
    "Fully remote ASP.NET Core PostgreSQL EF Core REST API"));
  Assert.Equal(FitBucket.LikelyFit,unknown.Bucket);
  Assert.Contains(unknown.Warnings,w=>w.Contains("Актуальность"));
 }

 [Fact]
 public void IrrelevantLanguagesAreNotPromoted()
 {
  var a=VacancyTriage.Assess(Job("Middle Java Backend Developer",
    "Fully remote. Spring Boot, REST services, PostgreSQL"));
  Assert.Equal(FitBucket.Excluded,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("C#/.NET"));
 }
}
