using JobRadar;
using Xunit;
namespace JobRadar.Tests;
public sealed class CoverageDriftTests
{
 [Fact]
 public void LargeSuddenDropIsFlagged()
 {
  Assert.Contains("Coverage anomaly",CoverageDrift.Warning(80,20));
  Assert.Contains("Coverage anomaly",CoverageDrift.Warning(80,40));
  Assert.Contains("Coverage anomaly",CoverageDrift.Warning(40,0));
 }
 [Fact]
 public void NormalFluctuationsAndSmallSamplesAreNotFlagged()
 {
  Assert.Null(CoverageDrift.Warning(80,41));
  Assert.Null(CoverageDrift.Warning(8,0));
  Assert.Null(CoverageDrift.Warning(80,80));
  Assert.Null(CoverageDrift.Warning(0,0));
 }
 [Fact]
 public async Task PreviousSourceCountsReadFromPersistedCrawlRuns()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);await storage.Initialize(CancellationToken.None);
  var source="test-drift-"+Guid.NewGuid().ToString("N");
  var now=DateTimeOffset.UtcNow;
  await storage.SaveReport(new CrawlReport(now,now,[
    new SourceResult(source,2,42,42,0,42,"QUERY_RECONCILED",null)]),CancellationToken.None);
  var prior=await storage.GetPreviousReferenceCountsAsync(CancellationToken.None);
  Assert.Equal(42,prior[source]);
 }
}
