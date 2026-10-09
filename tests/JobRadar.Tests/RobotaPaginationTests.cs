using System.Net;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;
public sealed class RobotaPaginationTests
{
 [Fact]
 public async Task ThreeDistinctPagesReconcileReportedTotal()
 {
  string? cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrEmpty(cs))return;
  var company=Random.Shared.Next(900000,999999);
  var start=Random.Shared.Next(5000000,8000000);
  var ids=Enumerable.Range(start,7).ToArray();
  var records=ids.Select(id=>new {id,notebookId=company,name="Middle .NET",companyName="Test"}).ToArray();
  string Page(int index)
  {
   var subset=index switch {0=>records.Take(3),1=>records.Skip(3).Take(3),2=>records.Skip(6),_=>records.Take(0)};
   return JsonSerializer.Serialize(new {total=7,documents=subset});
  }
  var details=JsonSerializer.Serialize(new {totalVacanciesCount=7,filteredVacancies=
    ids.Select(id=>new {id,description=new string('Z',180)}).ToArray()});
  int companyCalls=0,searchCalls=0;
  using var client=new HttpClient(new Stub(req=>{
   string body;
   if(req.RequestUri!.AbsolutePath.Contains("/published-vacancies"))
   {companyCalls++;body=details;}
   else
   {
    searchCalls++;
    var value=req.RequestUri.Query.Split('&').FirstOrDefault(s=>s.StartsWith("page=",StringComparison.Ordinal));
    var page=value is null?0:int.Parse(value[5..]);
    body=Page(page);
   }
   return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)};
  }));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var source=new RobotaApiSource("paging-test-"+Guid.NewGuid().ToString("N"));
  var report=await new Crawler(new HttpFetcher(client,0),storage,
       new RadarOptions{MaxPagesPerSource=3}).RunAsync([source],CancellationToken.None);
  var result=Assert.Single(report.Sources);
  Assert.Equal("QUERY_RECONCILED",result.Status);
  Assert.Equal(3,result.PagesFetched);
  Assert.Equal(7,result.ReferencesFound);
  Assert.Equal(7,result.DetailsFetched);
  Assert.Equal(0,result.DroppedRecords);
  Assert.Equal(3,searchCalls);
  Assert.Equal(1,companyCalls);
 }

 [Fact]
 public async Task DuplicatedPageIsPartialNotSuccess()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrEmpty(cs))return;
  var company=Random.Shared.Next(900000,999999);
  var id=Random.Shared.Next(7000000,8000000);
  var payload=JsonSerializer.Serialize(new {total=2,documents=new []{
    new {id,notebookId=company,name="Middle .NET"}
  }});
  var details=JsonSerializer.Serialize(new {filteredVacancies=new []{
    new {id,description=new string('Y',150)}
  }});
  using var client=new HttpClient(new Stub(req=>
    new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(
     req.RequestUri!.AbsolutePath.Contains("/published-vacancies")?details:payload)}));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var source=new RobotaApiSource("duplicates-"+Guid.NewGuid().ToString("N"));
  var report=await new Crawler(new HttpFetcher(client,0),storage,
    new RadarOptions{MaxPagesPerSource=3}).RunAsync([source],CancellationToken.None);
  var result=Assert.Single(report.Sources);
  Assert.Equal(2,result.PagesFetched);
  Assert.Equal(1,result.ReferencesFound);
  Assert.Equal(2,result.ReportedTotal);
  Assert.Equal("PARTIAL",result.Status);
 }
 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> callback):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
   => Task.FromResult(callback(request));
 }
}
