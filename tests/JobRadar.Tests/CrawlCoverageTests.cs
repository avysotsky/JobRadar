using System.Net;
using System.Net.Http;
using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

public sealed class CrawlCoverageTests
{
 [Fact]
 public async Task ReconcilesTwoQueriesWithoutDuplicatingTheSameRobotaVacancy()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var jobId=Random.Shared.Next(10000000,99999999);
  var companyId=Random.Shared.Next(100000,999999);
  var url="https://robota.ua/company"+companyId+"/vacancy"+jobId;
  var payload=System.Text.Json.JsonSerializer.Serialize(new {
    total=1,documents=new[]{new {
      id=jobId,notebookId=companyId,name="Middle .NET API developer",
      companyName="Unit Test Employer",shortDescription="C# .NET ASP.NET Core"
    }}
  });
  var companyJson=System.Text.Json.JsonSerializer.Serialize(new {
    filteredVacancies=new[]{new {id=jobId,description=new string('X',200),date="2026-10-09T08:00:00Z"}}
  });
  using var http=new HttpClient(new StubHandler(req=>
   req.RequestUri!.AbsolutePath.Contains("/published-vacancies")?companyJson:payload));
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);
  var crawler=new Crawler(new HttpFetcher(http,0),store,new RadarOptions{MaxPagesPerSource=1});
  var report=await crawler.RunAsync([new RobotaApiSource(".net"),new RobotaApiSource("backend")],CancellationToken.None);
  Assert.Equal(2,report.Sources.Count);
  Assert.All(report.Sources,s=>{
    Assert.Equal("QUERY_RECONCILED",s.Status);
    Assert.Equal(1,s.ReferencesFound);
    Assert.Equal(1,s.DetailsFetched+s.CachedDetails);
    Assert.Equal(0,s.DetailsFailed);
  });
  Assert.Equal(1,report.Sources.Sum(x=>x.DetailsFetched));
  Assert.Equal(1,report.Sources.Sum(x=>x.CachedDetails));
  await using var db=new NpgsqlConnection(cs);
  await db.OpenAsync();
  await using(var cmd=new NpgsqlCommand(
    "SELECT count(*) FROM jobs WHERE source='robota' AND url=@u",db))
  {
   cmd.Parameters.AddWithValue("u",url);
   Assert.Equal(1L,await cmd.ExecuteScalarAsync());
  }
  await using(var cmd=new NpgsqlCommand(
    "SELECT count(*) FROM job_queries WHERE source='robota' AND url=@u",db))
  {
   cmd.Parameters.AddWithValue("u",url);
   Assert.Equal(2L,await cmd.ExecuteScalarAsync());
  }
  await using(var cmd=new NpgsqlCommand(
    "SELECT length(description),full_text_at IS NOT NULL FROM jobs WHERE source='robota' AND url=@u",db))
  {
   cmd.Parameters.AddWithValue("u",url);
   await using var reader=await cmd.ExecuteReaderAsync();
   Assert.True(await reader.ReadAsync());
   Assert.Equal(200,reader.GetInt32(0));
   Assert.True(reader.GetBoolean(1));
  }
 }

 private sealed class StubHandler(Func<HttpRequestMessage,string> response):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
   =>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
   {Content=new StringContent(response(request))});
 }
}
