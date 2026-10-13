using System.Net;
using System.Text;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class RemotiveCoreTests
{
 private const string ApiResponse="""
 {
  "jobs":[
   {"id":7001,"url":"https://remotive.com/remote-jobs/software-dev/csharp-mid-7001",
    "title":"Middle C# .NET Backend Developer","company_name":"Engineering Example",
    "candidate_required_location":"Worldwide",
    "publication_date":"2026-10-12T07:00:00",
    "description":"<p>Fully remote ASP.NET Core EF Core PostgreSQL Web API.</p>"},
   {"id":7002,"url":"https://remotive.com/remote-jobs/software-dev/dotnet-regional-7002",
    "title":"Middle .NET Backend Developer","company_name":"Regional Example",
    "candidate_required_location":"USA only",
    "description":"<p>Fully remote ASP.NET Core Web API SQL.</p>"},
   {"id":7003,"url":"https://remotive.com/remote-jobs/software-dev/otherstack-7003",
    "title":"Java Backend Developer","company_name":"Other Stack",
    "description":"<p>Spring Boot microservices.</p>"}
  ]
 }
 """;

 private static DateTimeOffset TestDay()=>
  new(2035,1,1,0,0,0,TimeSpan.Zero)
   .AddDays(Random.Shared.Next(0,2500));

 [Fact]
 public async Task CoreRemotiveIsBoundedAndPersistsOnlyRelevantPreviewsAndLocation()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);
  int calls=0;
  using var client=new HttpClient(new Stub(req=>{
   calls++;
   Assert.Equal(HttpMethod.Get,req.Method);
   Assert.Equal(RemoteBoards.RemotiveApi,req.RequestUri!.AbsoluteUri);
   return Ok(ApiResponse);
  }));
  var day=TestDay();
  var a=await RemotiveCoreRunner.RunAsync(client,store,day,CancellationToken.None);
  Assert.Equal("PARTIAL",a.Status);
  Assert.Equal(1,a.PagesFetched);
  Assert.Equal(2,a.ReferencesFound);
  Assert.Equal(3,a.RawRecords);
  Assert.Equal(0,a.DroppedRecords);
  Assert.Equal(0,a.DetailsFetched);
  Assert.Equal(0,a.DetailsFailed);
  Assert.Equal(1,calls);

  var records=await store.ReadVacanciesForTriageAsync(50000,CancellationToken.None);
  var global=Assert.Single(records,x=>x.Source=="remotive"&&x.Url.EndsWith("csharp-mid-7001"));
  Assert.False(global.HasFullText);
  Assert.Null(global.IsOpen);
  Assert.Equal("Worldwide",global.CandidateRequiredLocation);
  Assert.Equal(FitBucket.NeedsReview,VacancyTriage.Assess(global).Bucket);
  Assert.Contains("remotive-software-dev",global.Queries);
  var regional=Assert.Single(records,x=>x.Source=="remotive"&&x.Url.EndsWith("dotnet-regional-7002"));
  Assert.Equal("USA only",regional.CandidateRequiredLocation);
  Assert.Contains(VacancyTriage.Assess(regional).Warnings,w=>w.Contains("Украины"));
  Assert.DoesNotContain(records,x=>x.Url.EndsWith("otherstack-7003"));

  var blocked=await RemotiveCoreRunner.RunAsync(client,store,
   day.AddHours(1),CancellationToken.None);
  Assert.Equal("SKIPPED_QUOTA",blocked.Status);
  Assert.Equal(1,calls);
  var second=await RemotiveCoreRunner.RunAsync(client,store,
   day.AddHours(8),CancellationToken.None);
  Assert.Equal("PARTIAL",second.Status);
  Assert.Equal(2,calls);
  var exhausted=await RemotiveCoreRunner.RunAsync(client,store,
   day.AddHours(9),CancellationToken.None);
  Assert.Equal("SKIPPED_QUOTA",exhausted.Status);
  Assert.Equal(2,calls);
  var nextDay=await RemotiveCoreRunner.RunAsync(client,store,
   day.AddDays(1),CancellationToken.None);
  Assert.Equal("PARTIAL",nextDay.Status);
  Assert.Equal(3,calls);
 }

 [Fact]
 public async Task ConcurrentReservationsCannotExceedConservativeQuota()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);
  var instant=TestDay();
  var results=await Task.WhenAll(Enumerable.Range(0,12)
   .Select(_=>store.TryReserveRemotiveAsync(instant,CancellationToken.None)));
  Assert.Equal(1,results.Count(v=>v));
  Assert.Equal(11,results.Count(v=>!v));
 }

 [Fact]
 public async Task UtcDayBoundaryCannotResetGlobalEightHourCooldown()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);
  var late=TestDay().AddHours(23);
  Assert.True(await store.TryReserveRemotiveAsync(late,CancellationToken.None));
  Assert.False(await store.TryReserveRemotiveAsync(late.AddHours(2),CancellationToken.None));
  Assert.True(await store.TryReserveRemotiveAsync(late.AddHours(8),CancellationToken.None));
 }

 [Fact]
 public async Task ProviderFailureStillConsumesTheDailyReservation()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);
  int calls=0;
  using var http=new HttpClient(new Stub(_=>{
   calls++;
   return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
  }));
  var t=TestDay();
  var failed=await RemotiveCoreRunner.RunAsync(http,store,t,CancellationToken.None);
  Assert.Equal("FAILED",failed.Status);
  Assert.Equal("InvalidDataException",failed.Error);
  var skipped=await RemotiveCoreRunner.RunAsync(http,store,t.AddMinutes(1),CancellationToken.None);
  Assert.Equal("SKIPPED_QUOTA",skipped.Status);
  Assert.Equal(1,calls);
 }

 [Fact]
 public async Task AdditionalSourceAppearsInOrdinaryPersistedCrawlReport()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);
  using var http=new HttpClient(new Stub(_=>Ok(ApiResponse)));
  var crawler=new Crawler(new HttpFetcher(http,0),store,
   new RadarOptions{RetryAfterCrawl=false});
  var report=await crawler.RunAsync([],CancellationToken.None,
   _=>Task.FromResult(new SourceResult("remotive",1,2,0,0,null,
    "PARTIAL",null,RawRecords:3,DroppedRecords:0)));
  var source=Assert.Single(report.Sources);
  Assert.Equal("remotive",source.Source);
  Assert.Equal(2,source.ReferencesFound);
  Assert.Equal(3,source.RawRecords);
  var persisted=await store.ReadLatestCrawlReportAsync(CancellationToken.None);
  Assert.NotNull(persisted);
  Assert.Contains(persisted!.Sources,x=>x.Source=="remotive"&&x.ReferencesFound==2);
 }

 private static HttpResponseMessage Ok(string json)=>
  new(HttpStatusCode.OK)
  {Content=new StringContent(json,Encoding.UTF8,"application/json")};
 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(
   HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(respond(request));
 }
}
