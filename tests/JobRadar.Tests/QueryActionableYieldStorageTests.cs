using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class QueryActionableYieldStorageTests
{
 [Fact]
 public async Task SavedQueryDiscoveriesAndCurrentDecisionsProduceBoundedMarginalYield()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;

  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var id=Guid.NewGuid().ToString("N");
  var firstQuery="dou-rss-phase36-first-"+id;
  var secondQuery="dou-rss-phase36-second-"+id;
  var first=new JobRef("dou","https://jobs.dou.ua/vacancies/"+id+"a/",
   "Middle .NET Backend",null,DateTimeOffset.UtcNow,
   "Fully remote ASP.NET Core Web API EF Core PostgreSQL.");
  var second=new JobRef("dou","https://jobs.dou.ua/vacancies/"+id+"b/",
   "Middle C# Backend",null,DateTimeOffset.UtcNow,
   "Fully remote ASP.NET Core Web API PostgreSQL.");

  var started=DateTimeOffset.UtcNow.AddMinutes(-1);
  await storage.SaveDiscovered(first,CancellationToken.None,firstQuery);
  await storage.SaveDiscovered(first,CancellationToken.None,secondQuery);
  await storage.SaveDiscovered(second,CancellationToken.None,secondQuery);
  await storage.MarkEmploymentDecisionAsync(second.Url,"Applied",CancellationToken.None);
  var ended=DateTimeOffset.UtcNow.AddMinutes(1);
  var events=await storage.ReadDiscoveryGapObservationsAsync(
   started,ended,[firstQuery,secondQuery],50000,CancellationToken.None);
  Assert.Equal(3,events.Count);

  var run=new CrawlReport(started,ended,[
   new SourceResult(firstQuery,1,1,0,0,null,"PARTIAL",null,
    RawRecords:1,DroppedRecords:0),
   new SourceResult(secondQuery,1,2,0,0,null,"PARTIAL",null,
    RawRecords:2,DroppedRecords:0)
  ]);
  var gaps=DiscoveryGapAudit.Analyze(run,events);
  var jobs=(await storage.ReadVacanciesForTriageAsync(
   50000,CancellationToken.None)).Where(x=>x.Source=="dou" &&
     (x.Url==first.Url||x.Url==second.Url)).ToArray();
  Assert.Equal(2,jobs.Length);
  var decisions=await storage.ReadEmploymentDecisionsAsync(CancellationToken.None);
  var report=QueryActionableYield.Analyze(run,gaps,events,jobs,
   decisions,jobs.Length,DateTimeOffset.UtcNow);

  var baseline=Assert.Single(report.Queries,x=>x.Query==firstQuery);
  Assert.Equal(1,baseline.MarginalUrls);
  Assert.Equal(1,baseline.CurrentTargetBackendReview);
  var supplemental=Assert.Single(report.Queries,x=>x.Query==secondQuery);
  Assert.Equal(2,supplemental.ObservedUrls);
  Assert.Equal(1,supplemental.MarginalUrls);
  Assert.Equal(0,supplemental.CurrentTargetBackendReview);
  Assert.Equal(1,supplemental.CurrentExcluded);
  Assert.Equal(1,report.CurrentTargetBackendMarginalByProvider["dou"]);
  Assert.Equal(0,report.CurrentSnapshotOmitted);
 }
}
