using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class CoverageReviewStorageTests
{
 [Fact]
 public async Task PersistedReportIsAvailableToTheBoundedReadOnlyReview()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;

  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var sourceName="dou-rss-phase27-"+Guid.NewGuid().ToString("N");
  var now=DateTimeOffset.UtcNow;
  await storage.SaveReport(new CrawlReport(now.AddMinutes(-1),now,
   [new SourceResult(sourceName,1,2,0,0,null,"PARTIAL",null,
    RawRecords:3,DroppedRecords:1)]),CancellationToken.None);

  var runs=await storage.ReadRecentCrawlReportsAsync(100,CancellationToken.None);
  var stored=Assert.Single(runs.SelectMany(x=>x.Sources)
   .Where(x=>x.Source==sourceName));
  Assert.Equal(3,stored.RawRecords);
  Assert.Equal(1,stored.DroppedRecords);

  var review=CoverageReview.Analyze(runs,now.AddMinutes(1));
  var row=Assert.Single(review.Sources,x=>x.Source==sourceName);
  Assert.Equal("P1",row.Priority);
  Assert.Contains("RAW_ITEMS_DISCARDED",row.Signals);
 }

 [Theory]
 [InlineData(0)]
 [InlineData(101)]
 public async Task InvalidHistoryLimitsAreRejectedBeforeDatabaseAccess(int limit)
 {
  var storage=new Storage("");
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
   storage.ReadRecentCrawlReportsAsync(limit,CancellationToken.None));
 }
}
