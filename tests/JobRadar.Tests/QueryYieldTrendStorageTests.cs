using JobRadar;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace JobRadar.Tests;

public sealed class QueryYieldTrendStorageTests
{
 [Fact]
 public async Task ThreePersistedNonoverlappingRunsYieldComparableTrend()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var suffix=Guid.NewGuid().ToString("N");
  var query="dou-rss-phase37-"+suffix;
  var job=new JobRef("dou","https://jobs.dou.ua/vacancies/"+suffix+"/",
   "Middle .NET Backend",null,null,
   "Fully remote ASP.NET Core Web API EF Core PostgreSQL RabbitMQ.");
  await storage.SaveDiscovered(job,CancellationToken.None,query);
  await storage.Save(new JobDetail(job,
   "Fully remote ASP.NET Core Web API EF Core PostgreSQL RabbitMQ. "+
   new string('X',100),null,DateTimeOffset.UtcNow),
   CancellationToken.None);

  // SaveDiscovered emits a fresh event, so write three historical test
  // observations explicitly into the ISOLATED disposable test database.
  // The historical run windows do not overlap with that fresh event.
  var ends=new[]
  {
   DateTimeOffset.UtcNow.AddDays(-3),
   DateTimeOffset.UtcNow.AddDays(-2),
   DateTimeOffset.UtcNow.AddDays(-1)
  };
  await using(var db=new NpgsqlConnection(cs))
  {
   await db.OpenAsync();
   foreach(var ended in ends)
   {
    await using var cmd=new NpgsqlCommand("""
INSERT INTO job_discoveries(source,url,query_name,observed_at)
VALUES(@source,@url,@query,@at)
""",db);
    cmd.Parameters.AddWithValue("source","dou");
    cmd.Parameters.AddWithValue("url",job.Url);
    cmd.Parameters.AddWithValue("query",query);
    cmd.Parameters.AddWithValue("at",NpgsqlDbType.TimestampTz,
     ended.AddSeconds(-10).ToUniversalTime());
    await cmd.ExecuteNonQueryAsync();
    await storage.SaveReport(new CrawlReport(ended.AddMinutes(-1),ended,[
     new SourceResult(query,1,1,0,0,null,"PARTIAL",null,
      RawRecords:1,DroppedRecords:0)
    ]),CancellationToken.None);
   }
  }

  var recent=(await storage.ReadRecentCrawlReportsAsync(100,CancellationToken.None))
   .Where(x=>x.Sources.Any(s=>s.Source==query))
   .OrderByDescending(x=>x.EndedAt).ToArray();
  Assert.Equal(3,recent.Length);
  var snapshots=(await storage.ReadVacanciesForTriageAsync(
    50000,CancellationToken.None))
   .Where(x=>x.Source==job.Source && x.Url==job.Url).ToArray();
  var decisions=await storage.ReadEmploymentDecisionsAsync(CancellationToken.None);
  Assert.Single(snapshots);
  var evaluations=new List<QueryActionableYieldReport>();
  foreach(var run in recent)
  {
   var events=await storage.ReadDiscoveryGapObservationsAsync(
    run.StartedAt,run.EndedAt,[query],50000,CancellationToken.None);
   Assert.Single(events);
   evaluations.Add(QueryActionableYield.Analyze(
    run,DiscoveryGapAudit.Analyze(run,events),events,
    snapshots,decisions,snapshots.Length,DateTimeOffset.UtcNow));
  }
  var result=QueryYieldTrend.Analyze(evaluations,DateTimeOffset.UtcNow);
  var row=Assert.Single(result.Queries);
  Assert.Equal(query,row.Query);
  Assert.Equal(3,row.VerifiedRuns);
  Assert.Equal(3,row.SumMarginalUrls);
  Assert.Equal(3,row.SumCurrentTargetBackendReviews);
  Assert.Equal("REVIEW_RETAIN_TARGET_QUERY",row.Assessment);
  Assert.DoesNotContain("OVERLAPPING_CRAWL_WINDOWS",row.Signals);
 }
}
