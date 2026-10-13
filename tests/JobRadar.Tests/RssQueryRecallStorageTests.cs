using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class RssQueryRecallStorageTests
{
 [Fact]
 public async Task ReadsQueryOccurrencesAndReconcilesWithSavedDiscoveryCounts()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var unique=Guid.NewGuid().ToString("N");
  var firstQuery="dou-rss-first-"+unique;
  var extraQuery="dou-rss-ASP.NET Core-"+unique;
  var id=Random.Shared.Next(100000000,999999999);
  var url1=$"https://jobs.dou.ua/vacancies/{id}/";
  var url2=$"https://jobs.dou.ua/vacancies/{id+1}/";
  var started=DateTimeOffset.UtcNow.AddMinutes(-1);
  var job1=new JobRef("dou",url1,"Middle .NET",null,null);
  var job2=new JobRef("dou",url2,"Middle ASP.NET Core",null,null);
  await storage.SaveDiscovered(job1,CancellationToken.None,firstQuery);
  await storage.SaveDiscovered(job1,CancellationToken.None,extraQuery);
  await storage.SaveDiscovered(job2,CancellationToken.None,extraQuery);
  var ended=DateTimeOffset.UtcNow.AddMinutes(1);
  var observations=await storage.ReadRssQueryObservationsAsync(started,ended,
   [firstQuery,extraQuery],50000,CancellationToken.None);
  Assert.Equal(3,observations.Count);
  var report=new CrawlReport(started,ended,[
   new SourceResult(firstQuery,1,1,0,0,null,"PARTIAL",null,RawRecords:1,DroppedRecords:0),
   new SourceResult(extraQuery,1,2,0,0,null,"PARTIAL",null,RawRecords:2,DroppedRecords:0)]);
  var review=RssQueryRecall.Analyze(report,observations);
  Assert.Equal(new int?[]{1,1},review.Queries.Select(q=>q.IncrementalUrls).ToArray());
  Assert.Equal(2,review.IncrementalProviderTotals["dou"]);
  await Assert.ThrowsAsync<InvalidDataException>(()=>
   storage.ReadRssQueryObservationsAsync(started,ended,
    [firstQuery,extraQuery],2,CancellationToken.None));
 }

 [Theory]
 [InlineData(0)]
 [InlineData(50001)]
 public async Task InvalidRowLimitFailsBeforeDatabaseConnection(int maxRows)
 {
  var storage=new Storage("");
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
   storage.ReadRssQueryObservationsAsync(
    DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow,
    ["dou"],maxRows,CancellationToken.None));
 }

 [Fact]
 public async Task EmptyOrUnrelatedQueryListDoesNotContactDatabase()
 {
  var storage=new Storage("");
  var result=await storage.ReadRssQueryObservationsAsync(
   DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow,
   ["robota-api-.net"],5,CancellationToken.None);
  Assert.Empty(result);
 }
}
