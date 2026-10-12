using System.Net;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class RobotaTotalDriftTests
{
 [Theory]
 [InlineData(false, "total changed")]
 [InlineData(true, "total missing")]
 public async Task LaterPageWithChangedOrMissingTotalCannotClaimReconciliation(
  bool omitSecondTotal,string expectedError)
 {
  var connectionString=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(connectionString))return;

  var companyId=Random.Shared.Next(900000,999999);
  var firstId=Random.Shared.Next(8000000,9000000);
  var first=new {id=firstId,notebookId=companyId,name="Middle .NET Backend"};
  var second=new {id=firstId+1,notebookId=companyId,name="Middle C# API Developer"};
  var firstPage=JsonSerializer.Serialize(new {total=2,documents=new[]{first}});
  var secondPage=omitSecondTotal
   ?JsonSerializer.Serialize(new {documents=new[]{second}})
   :JsonSerializer.Serialize(new {total=3,documents=new[]{second}});
  var companyPage=JsonSerializer.Serialize(new
  {
   totalVacanciesCount=2,
   filteredVacancies=new[]
   {
    new {id=firstId,description=new string('A',150)},
    new {id=firstId+1,description=new string('B',150)}
   }
  });
  var searchRequests=0;
  using var client=new HttpClient(new Stub(request=>
  {
   var url=request.RequestUri!;
   var payload=url.AbsolutePath.Contains("/published-vacancies",
     StringComparison.Ordinal)
    ?companyPage
    :++searchRequests==1?firstPage:secondPage;
   return new HttpResponseMessage(HttpStatusCode.OK)
   {Content=new StringContent(payload)};
  }));
  var storage=new Storage(connectionString);
  await storage.Initialize(CancellationToken.None);
  var source=new RobotaApiSource("total-drift-"+Guid.NewGuid().ToString("N"));
  var options=new RadarOptions{MaxPagesPerSource=2,EnabledRobotaDetails=true};
  var report=await new Crawler(new HttpFetcher(client,0),storage,options)
   .RunAsync([source],CancellationToken.None);
  var result=Assert.Single(report.Sources);

  Assert.Equal(2,searchRequests);
  Assert.Equal(2,result.PagesFetched);
  Assert.Equal(2,result.ReferencesFound);
  Assert.Equal(2,result.DetailsFetched);
  Assert.Equal(2,result.ReportedTotal);
  Assert.Equal(0,result.DroppedRecords);
  Assert.Equal("PARTIAL",result.Status);
  Assert.Contains(expectedError,result.Error!,StringComparison.OrdinalIgnoreCase);
 }

 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> responder)
  :HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(
   HttpRequestMessage request,CancellationToken ct)=>
   Task.FromResult(responder(request));
 }
}
