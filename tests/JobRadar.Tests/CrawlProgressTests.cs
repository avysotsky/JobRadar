using System.Net;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class CrawlProgressTests
{
 [Fact]
 public async Task EmitsSourcePageDetailAndCompletionProgressDuringDatabaseBackedCrawl()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var id=Random.Shared.Next(10000000,99999999);
  var company=Random.Shared.Next(900000,999999);
  var listing=JsonSerializer.Serialize(new
  {
   total=1,
   documents=new[]{new{id,notebookId=company,name="Middle .NET Backend",companyName="Test"}}
  });
  var detail=JsonSerializer.Serialize(new
  {
   totalVacanciesCount=1,
   filteredVacancies=new[]{new{id,description=new string('X',200)}}
  });
  using var http=new HttpClient(new Stub(req=>new HttpResponseMessage(HttpStatusCode.OK)
  {
   Content=new StringContent(req.RequestUri!.AbsolutePath.Contains("/published-vacancies")
     ?detail:listing)
  }));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var messages=new List<string>();
  var source=new RobotaApiSource("progress-"+Guid.NewGuid().ToString("N"));
  var report=await new Crawler(new HttpFetcher(http,0),storage,
    new RadarOptions{MaxPagesPerSource=1},messages.Add)
      .RunAsync([source],CancellationToken.None);

  var result=Assert.Single(report.Sources);
  Assert.Equal("QUERY_RECONCILED",result.Status);
  Assert.Equal(1,result.DetailsFetched);
  Assert.Contains(messages,m=>m.Contains("START") && m.Contains("max pages=1"));
  Assert.Contains(messages,m=>m.Contains("fetching page 1/1"));
  Assert.Contains(messages,m=>m.Contains("returned 1 listings"));
  Assert.Contains(messages,m=>m.Contains("processed=1") && m.Contains("full=1"));
  Assert.Contains(messages,m=>m.Contains("FINISHED") && m.Contains("QUERY_RECONCILED"));
  // Progress metadata should not print individual vacancy addresses or content.
  Assert.DoesNotContain(messages,m=>m.Contains("https://"));
 }

 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
   =>Task.FromResult(respond(request));
 }
}
