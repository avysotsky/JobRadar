using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class Phase8EligibilityRegressionTests
{
 private sealed record Scenario(
  string Name,
  string Title,
  string Description,
  string ExpectedWorkMode,
  string ExpectedBucket,
  string? WarningContains,
  bool HasFullText);

 [Fact]
 public void MultilingualEvidenceCorpusPreservesAmbiguousCandidatesForReview()
 {
  var path=Path.Combine(AppContext.BaseDirectory,"Fixtures","phase8-eligibility.json");
  var scenarios=JsonSerializer.Deserialize<Scenario[]>(File.ReadAllText(path))
   ?? throw new InvalidDataException("Missing Phase 8 scenarios");
  Assert.True(scenarios.Length>=30,"Regression corpus must cover more than anecdotal examples");
  foreach(var test in scenarios)
  {
   var snapshot=new VacancySnapshot(
    "fixture","https://example.org/jobs/"+test.Name,
    test.Title,"Synthetic employer",test.Description,
    test.Description,test.HasFullText,null,DateTimeOffset.UtcNow,
    DateTimeOffset.UtcNow,["fixture-query"]);
   var assessment=VacancyTriage.Assess(snapshot);
   Assert.True(
    Enum.Parse<WorkMode>(test.ExpectedWorkMode)==assessment.WorkMode,
    $"{test.Name}: expected {test.ExpectedWorkMode}, got {assessment.WorkMode}");
   Assert.True(
    Enum.Parse<FitBucket>(test.ExpectedBucket)==assessment.Bucket,
    $"{test.Name}: expected {test.ExpectedBucket}, got {assessment.Bucket}. "+
    $"Reasons: {string.Join(", ",assessment.Reasons)}; "+
    $"Warnings: {string.Join(", ",assessment.Warnings)}");
   if(test.WarningContains is not null)
    Assert.Contains(assessment.Warnings,w=>
     w.Contains(test.WarningContains,StringComparison.OrdinalIgnoreCase));
  }
 }

 [Fact]
 public void OptionalRelocationAndTimeZoneDoNotClaimCountryIneligibility()
 {
  var optional=VacancyEligibility.Analyze("Middle .NET Backend",
   "Fully remote in Ukraine, EU timezone. Relocation support is optional. ASP.NET Core REST API.");
  Assert.False(optional.LocationRestricted);
  Assert.False(optional.MandatoryRelocation);
  Assert.True(optional.BackendEvidence);
  Assert.False(optional.RequiresReview);
 }

 [Fact]
 public void ResidencyAndRelocationAreReviewGatesNotAutomaticExclusions()
 {
  var signals=VacancyEligibility.Analyze("Middle C# Backend",
   "Remote within EU, mandatory relocation to Warsaw. ASP.NET Core REST API.");
  Assert.True(signals.LocationRestricted);
  Assert.True(signals.MandatoryRelocation);
  var assessment=VacancyTriage.Assess(new VacancySnapshot(
   "fixture","https://example.org/jobs/restriction",
   "Middle C# Backend","Example",null,
   "Remote within EU, mandatory relocation to Warsaw. ASP.NET Core Web API, PostgreSQL.",
   true,null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,[]));
  Assert.Equal(FitBucket.NeedsReview,assessment.Bucket);
 }

 [Fact]
 public void NoDotNetInIncompletePreviewCannotProveIrrelevance()
 {
  var snapshot=new VacancySnapshot(
   "rss","https://example.org/jobs/preview","Backend Engineer","Example",
   "Remote service development","",false,null,null,DateTimeOffset.UtcNow,[]);
  Assert.Equal(FitBucket.NeedsReview,VacancyTriage.Assess(snapshot).Bucket);
 }
}
