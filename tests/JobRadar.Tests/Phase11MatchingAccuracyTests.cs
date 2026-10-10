using JobRadar;
using Xunit;

namespace JobRadar.Tests;

/// <summary>
/// Synthetic regression cases modeled after observed classes of errors in a
/// private 265-vacancy audit. No provider vacancy descriptions are committed.
/// </summary>
public sealed class Phase11MatchingAccuracyTests
{
 private const string Stack="ASP.NET Core REST API PostgreSQL EF Core.";
 private static VacancyAssessment Assess(string title,string extra,
  bool fullText=true)
 {
  var text="Fully remote in Ukraine. "+Stack+" "+extra;
  var job=new VacancySnapshot(
   "fixture","https://example.invalid/vacancy/phase11",title,"Synthetic employer",
   fullText?null:text,fullText?text:"",fullText,null,
   DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,["test"]);
  return VacancyTriage.Assess(job);
 }

 [Theory]
 [InlineData("Mid-Senior .NET Backend Developer")]
 [InlineData("Mid/Senior C# Backend Developer")]
 [InlineData(".NET Mid to Senior Backend Developer")]
 [InlineData("Middle / Senior .NET Backend Developer")]
 public void MixedSeniorityMustRemainReviewRatherThanExcluded(string title)
 {
  var a=Assess(title,"Maintain web APIs.");
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("Смешанный"));
 }

 [Theory]
 [InlineData("We are looking for a Senior Product Engineer for this team.")]
 [InlineData("We are seeking an experienced Senior .NET Developer.")]
 [InlineData("About the role: As a Senior .NET Engineer, build APIs.")]
 [InlineData("Position: Senior Backend Developer.")]
 public void ExplicitSeniorRoleInBodyRequiresReview(string body)
 {
  var a=Assess("Middle .NET Backend Developer",body);
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("Senior/Lead-уровень роли"));
 }

 [Theory]
 [InlineData("Mentor senior colleagues on API integration.")]
 [InlineData("Lead design and development of REST endpoints.")]
 [InlineData("Coordinate with the Senior Product Engineer from another team.")]
 public void IncidentalLeadershipLanguageAloneDoesNotBlockBackend(string body)
 {
  Assert.Equal(FitBucket.LikelyFit,Assess("Middle .NET Backend Developer",body).Bucket);
 }

 [Theory]
 [InlineData("Middle FullStack Engineer (.NET, React.js)","ASP.NET Core and React are core work.")]
 [InlineData("Fullstack C# / JavaScript Engineer","Maintain APIs and browser applications.")]
 [InlineData("Product Engineer (.NET + React)","Develop APIs and user interfaces.")]
 [InlineData("Middle Frontend .NET Developer","API integrations and frontend development.")]
 public void FullstackOrFrontendTitleRequiresReview(string title,string body)
 {
  var a=Assess(title,body);
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("Fullstack/Frontend"));
 }

 [Theory]
 [InlineData("Strong proficiency with TypeScript and a modern frontend framework (React, Angular).")]
 [InlineData("Commercial frontend experience with React is essential.")]
 [InlineData("React.js experience required.")]
 [InlineData("Must have Angular experience in addition to C# backend.")]
 public void RequiredFrontendFrameworkInBodyRequiresReview(string body)
 {
  var a=Assess("Middle C# Backend Developer",body);
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("Fullstack/Frontend"));
 }

 [Theory]
 [InlineData(".NET MAUI Specialist")]
 [InlineData("Middle Xamarin Mobile Developer C#")]
 [InlineData("Middle WPF .NET Developer")]
 public void MobileDesktopTitleCannotBeConfidentBackend(string title)
 {
  var a=Assess(title,"Backend APIs are included, too.");
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("Mobile/Desktop"));
 }

 [Theory]
 [InlineData("Upper-Intermediate English or higher.")]
 [InlineData("English: Upper Intermediate is required.")]
 [InlineData("Advanced English is mandatory for daily meetings.")]
 [InlineData("B2 English for client calls.")]
 public void ReversedEnglishLevelWordOrderIsStillReview(string body)
 {
  var a=Assess("Middle C# Backend Developer",body);
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("B2+"));
 }

 [Fact]
 public void OptionalFrontendOrWrittenEnglishB1DoesNotBlock()
 {
  var a=Assess("Middle .NET Backend",
   "React is a nice to have, not mandatory. English B1 written communication.");
  Assert.Equal(FitBucket.LikelyFit,a.Bucket);
 }

 [Theory]
 [InlineData("Remote or hybrid work options available.")]
 [InlineData("Hybrid / remote opportunities available.")]
 [InlineData("Віддалена або гібридна робота.")]
 public void OptionalHybridWithoutOfficeCommitmentIsUnknownAndNeedsReview(string extra)
 {
  var a=Assess("Middle .NET Backend Developer",extra);
  Assert.Equal(WorkMode.Unknown,a.WorkMode);
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
 }

 [Theory]
 [InlineData("Remote or hybrid work, 2 days in office per week.")]
 [InlineData("Hybrid / remote, office attendance required.")]
 public void ExplicitAttendanceStillOverridesHybridAlternatives(string extra)
 {
  var a=Assess("Middle .NET Backend Developer",extra);
  Assert.Equal(WorkMode.Hybrid,a.WorkMode);
  Assert.Equal(FitBucket.Excluded,a.Bucket);
 }

 [Fact]
 public void ClearSeniorTitleRemainsExcludedButIncompletePreviewRemainsReview()
 {
  Assert.Equal(FitBucket.Excluded,
   Assess("Senior .NET Backend Engineer","REST APIs.").Bucket);
  Assert.Equal(FitBucket.NeedsReview,
   Assess("Middle .NET Backend Developer","React.js experience required.",false).Bucket);
 }
}
