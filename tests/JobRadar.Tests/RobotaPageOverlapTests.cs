using System.Net;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class RobotaPageOverlapTests
{
 [Theory]
 [InlineData(false,3)]
 [InlineData(true,2)]
 public async Task RepeatedIdsAcrossPagesRemainPartialEvenWhenTotalIsStable(
  bool repeatWholePage,int expectedUnique)
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var companyId=Random.Shared.Next(900000,999999);
  var firstId=Random.Shared.Next(7000000,8000000);
  var first=new {id=firstId,notebookId=companyId,name="Middle C# Backend"};
  var second=new {id=firstId+1,notebookId=companyId,name="Middle .NET API"};
  var third=new {id=firstId+2,notebookId=companyId,name="Middle ASP.NET Core"};
  var firstPage=JsonSerializer.Serialize(new {total=3,documents=new[]{first,second}});
  var secondPage=JsonSerializer.Serialize(new {total=3,documents=repeatWholePage
   ?new[]{first,second}:new[]{second,third}});
  var companyPage=JsonSerializer.Serialize(new {totalVacanciesCount=3,
   filteredVacancies=new[]
   {
    new {id=firstId,description=new string('A',150)},
    new {id=firstId+1,description=new string('B',150)},
    new {id=firstId+2,description=new string('C',150)}
   }});
  int searchCalls=0,companyCalls=0;
  using var client=new HttpClient(new Stub(req=>
  {
   var url=req.RequestUri!;
   string body;
   if(url.AbsolutePath.Contains("/published-vacancies",StringComparison.Ordinal))
   {
    companyCalls++;
    body=companyPage;
   }
   else
   {
    searchCalls++;
    body=url.Query.Contains("page=1",StringComparison.Ordinal)
     ?secondPage:firstPage;
   }
   return new HttpResponseMessage(HttpStatusCode.OK)
    {Content=new StringContent(body)};
  }));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var source=new RobotaApiSource("overlap-"+Guid.NewGuid().ToString("N"));
  var report=await new Crawler(new HttpFetcher(client,0),storage,
    new RadarOptions{MaxPagesPerSource=2,EnabledRobotaDetails=true})
   .RunAsync([source],CancellationToken.None);
  var result=Assert.Single(report.Sources);
  Assert.Equal("PARTIAL",result.Status);
  Assert.Contains("overlap",result.Error!,StringComparison.OrdinalIgnoreCase);
  Assert.Equal(2,result.PagesFetched);
  Assert.Equal(3,result.ReportedTotal);
  Assert.Equal(expectedUnique,result.ReferencesFound);
  Assert.Equal(expectedUnique,result.DetailsFetched);
  Assert.Equal(4,result.RawRecords);
  Assert.Equal(0,result.DroppedRecords);
  Assert.Equal(2,searchCalls);
  Assert.Equal(1,companyCalls);
 }

 [Fact]
 public async Task SameVacancyInIndependentQueriesIsNotARepeatedPage()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var companyId=Random.Shared.Next(900000,999999);
  var id=Random.Shared.Next(7000000,8000000);
  var page=JsonSerializer.Serialize(new {total=1,documents=new[]
  {
   new {id,notebookId=companyId,name="Middle .NET Backend"}
  }});
  var details=JsonSerializer.Serialize(new {totalVacanciesCount=1,
   filteredVacancies=new[]{new {id,description=new string('D',160)}}});
  int searchCalls=0,companyCalls=0;
  using var client=new HttpClient(new Stub(req=>
  {
   if(req.RequestUri!.AbsolutePath.Contains("/published-vacancies",
    StringComparison.Ordinal))
   {
    companyCalls++;
    return new HttpResponseMessage(HttpStatusCode.OK)
     {Content=new StringContent(details)};
   }
   searchCalls++;
   return new HttpResponseMessage(HttpStatusCode.OK)
    {Content=new StringContent(page)};
  }));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var nonce=Guid.NewGuid().ToString("N");
  var sources=new[]{new RobotaApiSource("backend-"+nonce),
   new RobotaApiSource("dotnet-"+nonce)};
  var report=await new Crawler(new HttpFetcher(client,0),storage,
   new RadarOptions{MaxPagesPerSource=1,EnabledRobotaDetails=true})
    .RunAsync(sources,CancellationToken.None);
  Assert.Equal(2,report.Sources.Count);
  Assert.All(report.Sources,result=>
  {
   Assert.Equal("QUERY_RECONCILED",result.Status);
   Assert.Null(result.Error);
   Assert.Equal(1,result.ReferencesFound);
  });
  Assert.Equal(2,searchCalls);
  Assert.Equal(1,companyCalls);
  Assert.Equal(1,report.Sources.Sum(x=>x.DetailsFetched));
  Assert.Equal(1,report.Sources.Sum(x=>x.CachedDetails));
 }

 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> handler)
  :HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(
   HttpRequestMessage request,CancellationToken ct)=>
   Task.FromResult(handler(request));
 }
}
