using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class EmploymentTrackingTests
{
 private static string UniqueUrl()=>
  "https://djinni.co/jobs/"+Guid.NewGuid().ToString("N")+"-dotnet/";

 [Theory]
 [InlineData("https://robota.ua/company00000000/vacancy00000000?utm_source=mail#apply",
             "https://robota.ua/company00000000/vacancy00000000")]
 [InlineData("https://www.djinni.co/jobs/1234-net-developer/?ref=private",
             "https://djinni.co/jobs/1234-net-developer")]
 [InlineData("https://jobs.dou.ua/companies/example/vacancies/123/?utm_campaign=x",
             "https://jobs.dou.ua/companies/example/vacancies/123")]
 public void CanonicalUrlsDiscardTrackingParameters(string input,string expected)=>
  Assert.Equal(expected,EmploymentTrackingPolicy.Url(input));

 [Theory]
 [InlineData("https://robota.ua.evil.example/company/vacancy123")]
 [InlineData("http://robota.ua/company/vacancy123")]
 [InlineData("https://someone:secret@djinni.co/jobs/123")]
 [InlineData("https://djinni.co:444/jobs/123")]
 [InlineData("https://jobs.dou.ua/companies/hello")]
 [InlineData("https://robota.ua/jobs")]
 public void UnsafeOrWrongProviderIsRejected(string input)=>
  Assert.Throws<ArgumentException>(()=>EmploymentTrackingPolicy.Url(input));

 [Fact]
 public void UnrecognizedEventsAndPrivateFieldsRejected()
 {
  Assert.Throws<ArgumentException>(()=>EmploymentTrackingPolicy.Event("AutoApply"));
  Assert.Throws<ArgumentException>(()=>EmploymentTrackingPolicy.Label(new string('x',181)));
  Assert.Throws<ArgumentException>(()=>EmploymentTrackingPolicy.Note("private\nnote"));
  Assert.False(EmploymentTrackingPolicy.TryUrl("https://example.org/job/1",out _));
 }

 [Fact]
 public async Task ValidationDoesNotOpenDatabase()
 {
  var storage=new Storage("Host=127.0.0.1;Port=1;Database=none;Username=none");
  await Assert.ThrowsAsync<ArgumentException>(()=>
    storage.SaveManualEmploymentAsync("https://example.com/job/1",null,CancellationToken.None));
 }

 [Fact]
 public async Task EmploymentApplicationLifecycleIsManualUniqueAndScoped()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var url=UniqueUrl();
  try
  {
   await storage.SaveManualEmploymentAsync(url,"C# backend opening",CancellationToken.None);
   var initial=Assert.Single(await storage.ReadTrackedEmploymentAsync(CancellationToken.None),
      x=>x.Url==EmploymentTrackingPolicy.Url(url));
   Assert.Equal("Saved",initial.CurrentStatus);
   await Assert.ThrowsAsync<InvalidOperationException>(()=>
    storage.RecordOwnEmploymentEventAsync(url,"Replied",null,CancellationToken.None));
   await storage.RecordOwnEmploymentEventAsync(url,"Applied","CV uploaded in browser",CancellationToken.None);
   await Assert.ThrowsAsync<InvalidOperationException>(()=>
    storage.RecordOwnEmploymentEventAsync(url,"Applied",null,CancellationToken.None));
   await storage.RecordOwnEmploymentEventAsync(url,"Replied","Employer responded",CancellationToken.None);
   var current=Assert.Single(await storage.ReadTrackedEmploymentAsync(CancellationToken.None),
      x=>x.Url==EmploymentTrackingPolicy.Url(url));
   Assert.Equal("Replied",current.CurrentStatus);
   Assert.NotNull(current.AppliedAt);
   Assert.Equal(2L,current.EventCount);
  }
  finally
  {
   Assert.True(await storage.DeleteTrackedEmploymentAsync(url,CancellationToken.None));
  }
 }

 [Fact]
 public async Task AppliedJobsRemainInAuditButLeaveActionableList()
 {
  var now=DateTimeOffset.UtcNow;
  var sent="https://robota.ua/company00000000/vacancy00000000";
  var fresh="https://djinni.co/jobs/9999-middle-net/";
  VacancySnapshot vacancy(string url)=>new("djinni",url,"Middle .NET Backend",
    null,null,"Fully remote. ASP.NET Core Web API PostgreSQL EF Core RabbitMQ REST API.",
    true,null,now,now,[]);
  var tracked=new TrackedEmployment(sent,"Owner applied",now,now,"Applied",now,1);
  using var all=new StringWriter();
  var args=new Dictionary<OpportunityInputKind,TextReader>();
  var normal=await OpportunityPreview.WriteAsync([vacancy(sent),vacancy(fresh)],
    new Dictionary<(string,string),JobDiscoveryHistory>(),[],args,all,
    CancellationToken.None,actionableOnly:false,trackedEmployment:[tracked]);
  Assert.Equal(2,normal.Employment);
  Assert.Equal(1,normal.EmploymentApplied);
  Assert.Equal(1,normal.EmploymentTracked);
  Assert.Equal(2,all.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries).Length);
  Assert.Contains("\"EmploymentStatus\":\"Applied\"",all.ToString());
  Assert.Contains("\"StatusEvidence\":\"NOT_VERIFIED\"",all.ToString());
  using var filtered=new StringWriter();
  var summary=await OpportunityPreview.WriteAsync([vacancy(sent),vacancy(fresh)],
    new Dictionary<(string,string),JobDiscoveryHistory>(),[],args,filtered,
    CancellationToken.None,actionableOnly:true,trackedEmployment:[tracked]);
  Assert.Equal(2,summary.Employment);
  Assert.DoesNotContain(sent,filtered.ToString());
  Assert.Contains(fresh,filtered.ToString());
 }

 [Fact]
 public async Task CliRequiresExplicitDeleteAndDoesNotEchoPrivateNote()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);await store.Initialize(CancellationToken.None);
  var url=UniqueUrl();
  using var output=new StringWriter();
  using var errors=new StringWriter();
  try
  {
   Assert.Equal(0,await EmploymentTrackerCli.RunAsync(store,
     ["--employment-track-add","--employment-url="+url,
      "--employment-label=Manual .NET position"],output,errors,CancellationToken.None));
   Assert.Equal(0,await EmploymentTrackerCli.RunAsync(store,
     ["--employment-track-event","--employment-url="+url,
      "--employment-event=Applied","--employment-note=private-detail"],output,errors,
     CancellationToken.None));
   Assert.DoesNotContain("private-detail",output.ToString());
   Assert.Equal(4,await EmploymentTrackerCli.RunAsync(store,
     ["--employment-track-delete","--employment-url="+url],output,errors,
     CancellationToken.None));
   Assert.Contains("--confirm-delete",errors.ToString());
  }
  finally {await store.DeleteTrackedEmploymentAsync(url,CancellationToken.None);}
 }
}
