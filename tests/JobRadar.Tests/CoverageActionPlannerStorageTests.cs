using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class CoverageActionPlannerStorageTests
{
 [Fact]
 public async Task PersistedReportAndDiscoveriesProduceSpecificOfflineFollowUp()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var db=new Storage(cs);
  await db.Initialize(CancellationToken.None);
  var id=Guid.NewGuid().ToString("N");
  var query="robota-api-phase35-"+id;
  var url="https://robota.ua/company12/vacancy"+id;
  var job=new JobRef("robota",url,
   "Middle .NET Backend",null,DateTimeOffset.UtcNow,
   "Fully remote ASP.NET Core Web API and PostgreSQL");
  var start=DateTimeOffset.UtcNow.AddMinutes(-1);
  await db.SaveDiscovered(job,CancellationToken.None,query);
  var end=DateTimeOffset.UtcNow.AddMinutes(1);
  var storedReport=new CrawlReport(start,end,[
   new SourceResult(query,1,1,0,0,3,"PARTIAL",null,
    RawRecords:1,DroppedRecords:0)]);
  await db.SaveReport(storedReport,CancellationToken.None);

  var saved=await db.ReadRecentCrawlReportsAsync(100,CancellationToken.None);
  var current=Assert.Single(saved.Where(r=>r.Sources.Any(s=>s.Source==query)));
  var events=await db.ReadDiscoveryGapObservationsAsync(
   start,end,[query],50000,CancellationToken.None);
  Assert.Single(events,x=>x.Provider=="robota" &&
   x.Query==query && x.Url==url);
  var now=DateTimeOffset.UtcNow;
  var coverage=CoverageReview.Analyze([current],now);
  var gap=DiscoveryGapAudit.Analyze(current,events);
  var sourceJobs=(await db.ReadVacanciesForTriageAsync(50000,CancellationToken.None))
   .Where(x=>x.Source=="robota" && x.Url==url).ToArray();
  var decisions=await db.ReadEmploymentDecisionsAsync(CancellationToken.None);
  var funnel=VacancyFunnel.Analyze(sourceJobs,decisions,sourceJobs.Length,now);
  var actions=CoverageActionPlanner.Analyze(coverage,gap,funnel,now);
  Assert.Contains(actions.Actions,x=>x.Code=="ROBOTA_SEARCH_PAGING_UNVERIFIED"
   && x.Query==query && x.Priority=="P1");
  Assert.DoesNotContain(actions.Actions,x=>x.Code=="DISCOVERY_COUNT_MISMATCH");
  Assert.True(actions.CompleteVacancySample);
 }
}
