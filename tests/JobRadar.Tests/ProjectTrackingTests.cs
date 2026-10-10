using System.Text.Json;
using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

public sealed class ProjectTrackingTests
{
 private static string UniqueUrl()=>
  "https://freelancehunt.com/project/manual-dotnet/"+Guid.NewGuid().ToString("N")+".html";

 [Theory]
 [InlineData("freelancehunt","https://freelancehunt.com/project/api/123.html?utm_campaign=abc#section",
  "https://freelancehunt.com/project/api/123.html")]
 [InlineData("freelancer","https://www.freelancer.com/projects/trading-bot-123/?ref=private#comments",
  "https://freelancer.com/projects/trading-bot-123")]
 public void SourceUrlsAreCanonicalAndDoNotRetainTrackingQueries(
  string source,string url,string expected)
 {
  Assert.Equal(expected,ProjectTrackerPolicy.Url(source,url));
 }

 [Theory]
 [InlineData("freelancehunt","https://evil.example/project/api/123")]
 [InlineData("freelancer","http://freelancer.com/projects/api-123")]
 [InlineData("freelancer","https://freelancer.com.evil.example/projects/api-123")]
 [InlineData("freelancer","https://user:password@freelancer.com/projects/api-123")]
 [InlineData("freelancer","https://freelancer.com:444/projects/api-123")]
 [InlineData("freelancehunt","https://freelancehunt.com/jobs")]
 public void UnsafeOrMismatchedProjectUrlIsRejected(string provider,string url)
 {
  Assert.Throws<ArgumentException>(()=>ProjectTrackerPolicy.Url(provider,url));
 }

 [Fact]
 public void ManualDataPolicyRejectsUnrecognizedProvidersAndHugeFields()
 {
  Assert.Throws<ArgumentException>(()=>ProjectTrackerPolicy.Provider("remotive"));
  Assert.Throws<ArgumentException>(()=>ProjectTrackerPolicy.Label(new string('x',181)));
  Assert.Throws<ArgumentException>(()=>ProjectTrackerPolicy.Note(new string('a',501)));
  Assert.Throws<ArgumentException>(()=>ProjectTrackerPolicy.Event("AutoBid"));
  Assert.Throws<ArgumentException>(()=>ProjectTrackerPolicy.Currency("BTC"));
  Assert.Throws<ArgumentOutOfRangeException>(()=>ProjectTrackerPolicy.Budget(-2m));
  Assert.Equal("USD",ProjectTrackerPolicy.Currency("usd"));
 }

 [Fact]
 public async Task NoDatabaseNeededForInputValidation()
 {
  var tracker=new Storage("Host=localhost;Port=1;Username=none;Database=none");
  await Assert.ThrowsAsync<ArgumentException>(()=>
   tracker.SaveManualProjectAsync("other","https://x.example",
    null,null,null,CancellationToken.None));
  await Assert.ThrowsAsync<ArgumentException>(()=>
   tracker.SaveManualProjectAsync("freelancehunt",UniqueUrl(),
    "Manual budget",10m,null,CancellationToken.None));
 }

 [Fact]
 public async Task ManualBookmarkAndApplicationHistoryAreUniqueAndDeletable()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var tracker=new Storage(cs);
  await tracker.Initialize(CancellationToken.None);
  var url=UniqueUrl();
  try
  {
   await tracker.SaveManualProjectAsync("freelancehunt",url,"My .NET integration lead",400m,"UAH",CancellationToken.None);
   await tracker.SaveManualProjectAsync("freelancehunt",url,"My .NET integration lead",null,null,CancellationToken.None);
   var initial=Assert.Single((await tracker.ReadTrackedProjectsAsync(CancellationToken.None))
    .Where(x=>x.Url==url));
   Assert.Equal("Saved",initial.CurrentStatus);
   Assert.Equal(400m,initial.OwnBudget);
   Assert.Equal(0L,initial.EventCount);

   await tracker.RecordOwnProjectEventAsync("freelancehunt",url,"Applied",
    "Sent proposal in the browser",CancellationToken.None);
   await Assert.ThrowsAsync<InvalidOperationException>(()=>
    tracker.RecordOwnProjectEventAsync("freelancehunt",url,"Applied",null,CancellationToken.None));
   await tracker.RecordOwnProjectEventAsync("freelancehunt",url,"Replied",
    "Client requested clarification",CancellationToken.None);
   var latest=Assert.Single((await tracker.ReadTrackedProjectsAsync(CancellationToken.None))
    .Where(x=>x.Url==url));
   Assert.Equal("Replied",latest.CurrentStatus);
   Assert.NotNull(latest.AppliedAt);
   Assert.Equal(2L,latest.EventCount);

   await using var db=new NpgsqlConnection(cs);
   await db.OpenAsync();
   await using(var cmd=new NpgsqlCommand(
    "SELECT count(*) FROM project_tracker_events WHERE provider='freelancehunt' AND url=@u",db))
   {
    cmd.Parameters.AddWithValue("u",url);
    Assert.Equal(2L,await cmd.ExecuteScalarAsync());
   }
  }
  finally
  {
   Assert.True(await tracker.DeleteTrackedProjectAsync(
    "freelancehunt",url,CancellationToken.None));
  }
  Assert.DoesNotContain(await tracker.ReadTrackedProjectsAsync(CancellationToken.None),x=>x.Url==url);
 }

 [Fact]
 public async Task ApplicationOutcomeCannotBeRecordedBeforeApplied()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var tracker=new Storage(cs);await tracker.Initialize(CancellationToken.None);
  var url=UniqueUrl();
  try
  {
   await tracker.SaveManualProjectAsync("freelancehunt",url,null,null,null,CancellationToken.None);
   await Assert.ThrowsAsync<InvalidOperationException>(()=>
    tracker.RecordOwnProjectEventAsync("freelancehunt",url,"Won",null,CancellationToken.None));
   Assert.Equal(0L,Assert.Single((await tracker.ReadTrackedProjectsAsync(CancellationToken.None))
    .Where(x=>x.Url==url)).EventCount);
  }
  finally{await tracker.DeleteTrackedProjectAsync("freelancehunt",url,CancellationToken.None);}
 }

 [Fact]
 public async Task CannotRecordApplicationEventForUnknownProject()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var tracker=new Storage(cs);await tracker.Initialize(CancellationToken.None);
  await Assert.ThrowsAsync<InvalidOperationException>(()=>
   tracker.RecordOwnProjectEventAsync("freelancehunt",UniqueUrl(),
    "Applied","Nothing was sent",CancellationToken.None));
 }

 [Fact]
 public async Task CommandDoesNotLeakOwnNotesAndNeedsDeleteConfirmation()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);await store.Initialize(CancellationToken.None);
  var url=UniqueUrl();
  using var output=new StringWriter();
  using var error=new StringWriter();
  try
  {
   var add=await ProjectTrackerCli.RunAsync(store,
    ["--project-track-add","--project-provider=freelancehunt","--project-url="+url,
     "--project-label=Own bookmarked lead","--project-budget=200","--project-currency=UAH"],
     output,error,CancellationToken.None);
   Assert.Equal(0,add);
   var action=await ProjectTrackerCli.RunAsync(store,
    ["--project-track-event","--project-provider=freelancehunt","--project-url="+url,
     "--project-event=Applied","--project-note=private-sample-sentence"],
     output,error,CancellationToken.None);
   Assert.Equal(0,action);
   Assert.DoesNotContain("private-sample-sentence",output.ToString());
   var premature=await ProjectTrackerCli.RunAsync(store,
    ["--project-track-delete","--project-provider=freelancehunt","--project-url="+url],
     output,error,CancellationToken.None);
   Assert.Equal(4,premature);
   Assert.Contains("--confirm-delete",error.ToString());
  }
  finally{await store.DeleteTrackedProjectAsync("freelancehunt",url,CancellationToken.None);}
 }

 [Fact]
 public async Task TrackedBookmarkAppearsInUnifiedPreviewWithUserStatus()
 {
  var date=new DateTimeOffset(2026,10,10,13,0,0,TimeSpan.Zero);
  var tracked=new TrackedProject("freelancehunt",
   "https://freelancehunt.com/project/automation/123.html",
   "Own trading project bookmark",250m,"UAH",date,date.AddHours(2),
   "Applied",date.AddHours(1),1);
  using var output=new StringWriter();
  var summary=await OpportunityPreview.WriteAsync([],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],
   new Dictionary<OpportunityInputKind,TextReader>(),output,
   CancellationToken.None,[tracked]);
  Assert.Equal(1,summary.FreelanceProjects);
  Assert.Equal(1,summary.FreelanceReview);
  Assert.Equal(1,summary.ManuallyTrackedProjects);
  using var json=JsonDocument.Parse(output.ToString());
  Assert.Equal("USER_RECORDED_APPLIED",
   json.RootElement.GetProperty("StatusEvidence").GetString());
  Assert.Equal("Review",json.RootElement.GetProperty("Bucket").GetString());
  Assert.Equal("UAH",json.RootElement.GetProperty("Currency").GetString());
 }

 [Fact]
 public async Task WwwProviderAliasIsDeduplicatedAgainstManualBookmark()
 {
  var url="https://www.freelancehunt.com/project/automation/333.html";
  var project=new FreelancehuntProject(333,url,"C# API automation",
   "C# .NET trading integration",200m,"UAH",["C#"],0,false,false,
   DateTimeOffset.UtcNow,"Open");
  var bookmark=new TrackedProject("freelancehunt",
   ProjectTrackerPolicy.Url("freelancehunt",url),
   "Manual project",null,null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,
   "Applied",DateTimeOffset.UtcNow,1);
  using var output=new StringWriter();
  var summary=await OpportunityPreview.WriteAsync([],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],
   new Dictionary<OpportunityInputKind,TextReader>
   {[OpportunityInputKind.Freelancehunt]=
     new StringReader(JsonlOutput.Serialize(FreelancehuntProjects.Rank(project)))},
   output,CancellationToken.None,[bookmark]);
  Assert.Equal(1,summary.TotalUnique);
  using var record=JsonDocument.Parse(output.ToString());
  Assert.Equal("USER_RECORDED_APPLIED",
   record.RootElement.GetProperty("StatusEvidence").GetString());
 }

 [Fact]
 public async Task ExistingFreelanceSnapshotIsOverlayedNotDuplicated()
 {
  var date=DateTimeOffset.UtcNow;
  var p=new FreelancehuntProject(123,
    "https://freelancehunt.com/project/automation/123.html",
    "C# broker API","Implement .NET API trading",250m,"UAH",
    ["C#"],4,false,false,date,"Open");
  var imported=FreelancehuntProjects.Rank(p);
  var tracked=new TrackedProject("freelancehunt",p.Url,
   "Personal bookmark",260m,"UAH",date,date,"Replied",date,2);
  using var output=new StringWriter();
  var result=await OpportunityPreview.WriteAsync([],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],
   new Dictionary<OpportunityInputKind,TextReader>{
     [OpportunityInputKind.Freelancehunt]=new StringReader(JsonlOutput.Serialize(imported))
   },output,CancellationToken.None,[tracked]);
  Assert.Equal(1,result.TotalUnique);
  Assert.Equal(1,result.FreelanceProjects);
  Assert.Single(output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries));
  using var json=JsonDocument.Parse(output.ToString());
  Assert.Equal("USER_RECORDED_REPLIED",
    json.RootElement.GetProperty("StatusEvidence").GetString());
  Assert.Equal(250m,json.RootElement.GetProperty("BudgetMinimum").GetDecimal());
 }
}
