using System.Text;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class RemoteReviewPriorityTests
{
 private static RemoteBoardOpening Wwr(string title,string excerpt,string? full=null)=>
  new("wwr-programming","https://weworkremotely.com/remote-jobs/example-review",
   title,null,excerpt,DateTimeOffset.UtcNow,null,null,null,
   false,"We Work Remotely"){FullDescription=full};

 private static RemoteBoardOpening Remotive(string title,string location,string description)=>
  new("remotive","https://remotive.com/remote-jobs/software-dev/example-review",
   title,"Example",description,DateTimeOffset.UtcNow,location,null,null,
   true,"Remotive"){FullDescription=description};

 [Theory]
 [InlineData("Sticker Mule: AI agent engineer","Build and connect AI agents",
  RemoteReviewPriority.AdjacentEngineering)]
 [InlineData("STEUART NUTRITION: Software Developer AI Coding","AI assisted coding",
  RemoteReviewPriority.AdjacentEngineering)]
 [InlineData("Wisevu: Web Development Project Manager","Manage client websites",
  RemoteReviewPriority.LowRelevance)]
 [InlineData("Glean: Application Security Engineer","Secure cloud",
  RemoteReviewPriority.LowRelevance)]
 [InlineData("Pinterest: Data Scientist II","Data models and A/B tests",
  RemoteReviewPriority.LowRelevance)]
 [InlineData("Collibra: CPS Account Program Manager","Customer retention",
  RemoteReviewPriority.LowRelevance)]
 [InlineData("Sticker Mule: Software engineer",
  "We use Go, TypeScript, React and GraphQL",
  RemoteReviewPriority.LowRelevance)]
 [InlineData("Example: Backend Engineer","Remote REST API microservices",
  RemoteReviewPriority.UnknownStack)]
 public void KeepsNonTargetRolesOutOfMainDotNetReviewQueue(
  string title,string text,RemoteReviewPriority expected)
 {
  var result=RemoteReviewTriage.Assess(Wwr(title,text),FitBucket.NeedsReview);
  Assert.Equal(expected,result.Priority);
  Assert.NotEmpty(result.Evidence);
 }

 [Fact]
 public void RemotiveExplicitLocationRestrictionTakesPrecedenceOverStack()
 {
  var opening=Remotive("Middle .NET Backend Engineer",
   "USA, UK, India, Australia, Singapore, Mexico",
   "Fully remote C# ASP.NET Core Web API");
  var result=RemoteReviewTriage.Assess(opening,FitBucket.NeedsReview);
  Assert.Equal(RemoteReviewPriority.LocationVerification,result.Priority);
  Assert.Contains(result.Evidence,x=>x.Contains("location",StringComparison.OrdinalIgnoreCase));
 }

 [Theory]
 [InlineData("Worldwide except Ukraine")]
 [InlineData("Europe, Ukraine")]
 [InlineData("Ukraine not eligible")]
 [InlineData("EMEA")]
 [InlineData("USA only")]
 public void AmbiguousOrRestrictedRemotiveRegionsRequireVerification(string location)
 {
  var result=RemoteReviewTriage.Assess(
   Remotive("Middle .NET Backend",location,"ASP.NET Core Web API"),
   FitBucket.NeedsReview);
  Assert.Equal(RemoteReviewPriority.LocationVerification,result.Priority);
 }

 [Theory]
 [InlineData("Worldwide")]
 [InlineData("Anywhere")]
 [InlineData("Global")]
 [InlineData("Ukraine")]
 [InlineData("Ukraine only")]
 public void ExplicitAllowlistedLocationDoesNotImplyRejection(string location)
 {
  var result=RemoteReviewTriage.Assess(
   Remotive("Middle .NET Backend",location,"ASP.NET Core REST API"),
   FitBucket.NeedsReview);
  Assert.Equal(RemoteReviewPriority.DotNetEvidence,result.Priority);
 }

 [Theory]
 [InlineData("Headquarters: Remote - United States. Backend services.")]
 [InlineData("Headquarters: Remote, US. Backend services.")]
 [InlineData("Headquarters: New York. Remote (US) - build APIs.")]
 public void WwrExplicitRemoteUsCueTriggersReviewNotEligibilityClaim(string description)
 {
  var result=RemoteReviewTriage.Assess(
   Wwr("Middle Backend Engineer",description+" ASP.NET Core"),
   FitBucket.NeedsReview);
  Assert.Equal(RemoteReviewPriority.LocationVerification,result.Priority);
  Assert.Contains(result.Evidence,x=>x.Contains("verification",StringComparison.OrdinalIgnoreCase));
 }

 [Fact]
 public void HeadquartersAloneIsNotAWorkLocationRestriction()
 {
  var result=RemoteReviewTriage.Assess(
   Wwr("Middle .NET Backend Developer",
    "Headquarters: New York, NY. Remote first engineering. ASP.NET Core API"),
   FitBucket.NeedsReview);
  Assert.Equal(RemoteReviewPriority.DotNetEvidence,result.Priority);
 }

 [Fact]
 public void LateDotNetEvidenceInEntireWwrRssOverridesPreviewOnlyStackUncertainty()
 {
  var full="Headquarters: Worldwide. Remote API engineering. "+
   new string('q',1300)+" Required ASP.NET Core and PostgreSQL.";
  var item=Wwr("Middle Backend Engineer","Headquarters: Worldwide. Remote API engineering.",
   full);
  var live=RemoteBoards.Assess(item);
  Assert.Equal(FitBucket.NeedsReview,live.Bucket);
  Assert.Equal(RemoteReviewPriority.DotNetEvidence,live.ReviewPriority);
  Assert.Contains("C#/.NET",string.Join(" ",live.Reasons));
  var serialized=JsonlOutput.Serialize(live);
  Assert.DoesNotContain("Required ASP.NET Core and PostgreSQL",serialized);
 }

 [Fact]
 public void ExcludedIsNotPromotedIntoManualReview()
 {
  var result=RemoteReviewTriage.Assess(
   Wwr("Senior .NET Architect","Remote ASP.NET Core"),FitBucket.Excluded);
  Assert.Equal(RemoteReviewPriority.NotApplicable,result.Priority);
 }

 [Fact]
 public async Task OfflineReviewUsesOnlyExistingFileAndExportsCompactShortlist()
 {
  var path=Path.Combine(Path.GetTempPath(),"jobradar-review-"+Guid.NewGuid().ToString("N")+".jsonl");
  var rows=new[]
  {
   new RemoteBoardCandidate(
    Wwr("Example: Middle .NET Backend","Remote ASP.NET Core Backend",
     null),FitBucket.NeedsReview,72,[],[]),
   new RemoteBoardCandidate(
    Wwr("Example: AI agent engineer","Build automation agents",
     null),FitBucket.NeedsReview,32,[],[]),
   new RemoteBoardCandidate(
    Remotive("Example: Backend Engineer","USA only","ASP.NET Core REST API"),
    FitBucket.NeedsReview,82,[],[]),
   new RemoteBoardCandidate(
    Wwr("Example: Program Manager","Manage stakeholder relationships",
     null),FitBucket.NeedsReview,20,[],[]),
   new RemoteBoardCandidate(
    Wwr("Example: Senior Backend Engineer","Remote .NET",
     null),FitBucket.Excluded,40,[],[])
  };
  try
  {
   await File.WriteAllTextAsync(path,
    string.Join("\n",rows.Select(JsonlOutput.Serialize))+"\n",
    new UTF8Encoding(false,true));
   using var writer=new StringWriter();
   var report=await RemoteReviewOffline.RunAsync(path,writer,CancellationToken.None);
   Assert.Equal(5,report.Total);
   Assert.Equal(4,report.NeedsReview);
   Assert.Equal(1,report.Excluded);
   Assert.Equal(1,report.ReviewBreakdown.DotNetEvidence);
   Assert.Equal(1,report.ReviewBreakdown.AdjacentEngineering);
   Assert.Equal(1,report.ReviewBreakdown.LocationVerification);
   Assert.Equal(1,report.ReviewBreakdown.LowRelevance);
   var lines=writer.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries);
   Assert.Equal(report.NeedsReview,lines.Length);
   using var first=JsonDocument.Parse(lines[0]);
   Assert.Equal("DotNetEvidence",
    first.RootElement.GetProperty("ReviewPriority").GetString());
   Assert.False(first.RootElement.TryGetProperty("Excerpt",out _));
   Assert.False(first.RootElement.TryGetProperty("FullDescription",out _));
   Assert.All(lines,line=>
   {
    using var doc=JsonDocument.Parse(line);
    Assert.NotNull(doc.RootElement.GetProperty("Url").GetString());
    Assert.Equal("UNVERIFIED_OPEN_AND_WORK_LOCATION",
     doc.RootElement.GetProperty("Status").GetString());
   });
  }
  finally {if(File.Exists(path))File.Delete(path);}
 }

 [Fact]
 public async Task OfflineReviewRefusesCorruptJsonlWithoutEmittingShortlist()
 {
  var path=Path.Combine(Path.GetTempPath(),"jobradar-review-bad-"+Guid.NewGuid().ToString("N")+".jsonl");
  try
  {
   await File.WriteAllBytesAsync(path,new byte[]{(byte)'{',0xff,(byte)'}',(byte)'\n'});
   using var writer=new StringWriter();
   await Assert.ThrowsAsync<InvalidDataException>(()=>
    RemoteReviewOffline.RunAsync(path,writer,CancellationToken.None));
   Assert.Equal("",writer.ToString());
  }
  finally {if(File.Exists(path))File.Delete(path);}
 }

 [Fact]
 public void ReviewBreakdownNeverCountsExcludedVacancies()
 {
  var open=Wwr("Example: Backend Engineer","Remote Web API");
  var relevant=RemoteBoards.Assess(open);
  var excluded=RemoteBoards.Assess(Wwr("Senior .NET Architect","Remote ASP.NET Core"));
  var summary=RemoteReviewTriage.Summarize([relevant,excluded]);
  Assert.Equal(1,summary.TotalNeedsReview);
  Assert.Equal(1,summary.UnknownStack);
  Assert.Equal(0,summary.DotNetEvidence);
  Assert.Equal(0,summary.LowRelevance);
 }
}
