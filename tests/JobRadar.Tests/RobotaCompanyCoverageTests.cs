using System.Net;
using System.Text.Json;
using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

public sealed class RobotaCompanyCoverageTests
{
 [Fact]
 public void Detects100RecordCapAndDoesNotInventClosure()
 {
  var records=Enumerable.Range(1,100).Select(id=>new {
    id, description="<p>"+new string('D',160)+"</p>",date="2026-10-09T12:00:00Z"
  }).ToArray();
  var json=JsonSerializer.Serialize(new {totalVacanciesCount=139,filteredVacancies=records});
  var feed=RobotaCompanyDetails.ParseCompanyResult(json);
  Assert.Equal(100,feed.RawRecords);
  Assert.Equal(139,feed.ReportedTotal);
  Assert.True(feed.PossiblyTruncated);
  Assert.Equal(100,feed.Details.Count);
  Assert.False(feed.Details.ContainsKey(7777));
 }
 [Fact]
 public async Task MissingFromTruncatedFeedThrowsExplicitUnknownNotClosed()
 {
  var docs=Enumerable.Range(1,100).Select(id=>new {id,description=new string('X',180)}).ToArray();
  var json=JsonSerializer.Serialize(new{totalVacanciesCount=180,filteredVacancies=docs});
  using var client=new HttpClient(new Stub(_=>new HttpResponseMessage(HttpStatusCode.OK){
    Content=new StringContent(json)}));
  var feed=new RobotaCompanyDetails(new HttpFetcher(client,0));
  var job=new JobRef("robota","https://robota.ua/company111/vacancy7777","Test",null,null);
  var ex=await Assert.ThrowsAsync<InvalidDataException>(()=>feed.GetAsync(job,CancellationToken.None));
  Assert.Contains("potentially truncated",ex.Message);
  Assert.Contains("NOT evidence vacancy closed",ex.Message);
 }
 [Fact]
 public async Task RawRecordDropsMarkSearchPartialEvenWhenHttpSucceeds()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var id=Random.Shared.Next(9000000,9999999);
  var company=Random.Shared.Next(900000,999999);
  var payload=JsonSerializer.Serialize(new {total=2,documents=new object[]{
    new {id,notebookId=company,name="Middle .NET",companyName="Test"},
    new {id=0,notebookId=0,name="Malformed provider entry",companyName="Test"}
  }});
  var detail=JsonSerializer.Serialize(new {totalVacanciesCount=1,
    filteredVacancies=new[]{new{id,description=new string('Y',200)}}});
  using var client=new HttpClient(new Stub(r=>new HttpResponseMessage(HttpStatusCode.OK){
    Content=new StringContent(r.RequestUri!.AbsolutePath.Contains("/published-vacancies")?detail:payload)
  }));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var crawler=new Crawler(new HttpFetcher(client,0),storage,new RadarOptions{MaxPagesPerSource=1});
  var query="robota-malformed-"+Guid.NewGuid().ToString("N");
  // Unique query name means historical source count cannot affect this assertion.
  var report=await crawler.RunAsync([new RobotaApiSource(query)],CancellationToken.None);
  var result=Assert.Single(report.Sources);
  Assert.Equal(2,result.RawRecords);
  Assert.Equal(1,result.DroppedRecords);
  Assert.Equal(1,result.ReferencesFound);
  Assert.Equal(1,result.DetailsFetched);
  Assert.Equal("PARTIAL",result.Status);
  Assert.Contains("invalid vacancy identifiers",result.Error);
 }
 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    =>Task.FromResult(reply(request));
 }
}
