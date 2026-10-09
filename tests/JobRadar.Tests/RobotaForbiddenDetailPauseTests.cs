using System.Net;
using System.Text.Json;
using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

public sealed class RobotaForbiddenDetailPauseTests
{
 [Fact]
 public async Task DisabledRobotaDetailsKeepDiscoveriesAndNeverCallForbiddenEndpoint()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var id=Random.Shared.Next(10000000,99999999);
  var company=Random.Shared.Next(900000,999999);
  var listing=JsonSerializer.Serialize(new {
   total=1,
   documents=new[]{new {
    id,notebookId=company,name="Middle .NET Backend",
    companyName="Test Employer",shortDescription="ASP.NET Core Web API preview"
   }}
  });
  int listingCalls=0,detailCalls=0;
  using var http=new HttpClient(new Stub(req=>{
   if(req.RequestUri!.AbsolutePath.Contains("/published-vacancies"))
   {
    detailCalls++;
    return new HttpResponseMessage(HttpStatusCode.Forbidden);
   }
   listingCalls++;
   return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(listing)};
  }));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var progress=new List<string>();
  var opts=new RadarOptions{MaxPagesPerSource=1,EnabledRobotaDetails=false};
  var crawler=new Crawler(new HttpFetcher(http,0),storage,opts,progress.Add);
  var report=await crawler.RunAsync([new RobotaApiSource("paused-"+Guid.NewGuid().ToString("N"))],
    CancellationToken.None);
  var result=Assert.Single(report.Sources);

  Assert.Equal(1,listingCalls);
  Assert.Equal(0,detailCalls);
  Assert.Equal(1,result.ReferencesFound);
  Assert.Equal(0,result.DetailsFetched);
  Assert.Equal(0,result.DetailsFailed);
  Assert.Equal("PARTIAL",result.Status);
  Assert.Contains(progress,p=>p.Contains("Robota full text paused"));

  var url=$"https://robota.ua/company{company}/vacancy{id}";
  await using var db=new NpgsqlConnection(cs);
  await db.OpenAsync();
  await using var cmd=new NpgsqlCommand(
   "SELECT preview,description,full_text_at FROM jobs WHERE source='robota' AND url=@u",db);
  cmd.Parameters.AddWithValue("u",url);
  await using var reader=await cmd.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Contains("ASP.NET Core",reader.GetString(0));
  Assert.Equal("",reader.GetString(1));
  Assert.True(reader.IsDBNull(2));
 }

 [Fact]
 public async Task DisabledRobotaDetailsNeverRetryEvenWhenJobsAreQueued()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var id=Guid.NewGuid().ToString("N");
  var url="https://robota.ua/company999999/vacancy"+Random.Shared.Next(10000000,99999999);
  await storage.SaveDiscovered(new JobRef("robota",url,"Middle .NET","Test",null),
    CancellationToken.None);
  var requests=0;
  using var http=new HttpClient(new Stub(_=>{
    requests++;
    return new HttpResponseMessage(HttpStatusCode.Forbidden);
  }));
  var opts=new RadarOptions{EnabledRobota=true,EnabledRobotaDetails=false,
    EnabledDou=false,EnabledDjinni=false,EnabledWorkUa=false};
  var retry=new PendingRetry(new HttpFetcher(http,0),storage,opts);
  var report=await retry.RunAsync(CancellationToken.None);
  Assert.Equal(0,report.Attempted);
  Assert.Equal(0,requests);
  var current=await retry.StatusAsync(CancellationToken.None);
  Assert.True(current.Total>=1);
 }

 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
   =>Task.FromResult(response(request));
 }
}
