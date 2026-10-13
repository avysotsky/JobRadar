using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class CoverageActionPlannerTests
{
 private static readonly DateTimeOffset Now=
  new(2026,10,13,15,0,0,TimeSpan.Zero);
 private static SourceResult Row(string query,int refs=1,
  string status="PARTIAL",int pages=1,int? total=null,
  int? dropped=0,string? error=null)=>
  new(query,pages,refs,0,0,total,status,error,RawRecords:refs,
   DroppedRecords:dropped);
 private static CrawlReport Report(DateTimeOffset ended,params SourceResult[] sources)=>
  new(ended.AddMinutes(-1),ended,sources);
 private static DiscoveryGapObservation Event(string provider,string query,string id)=>
  new(provider,query,"https://example.test/"+id);
 private static VacancyFunnelRow FunnelRow(string provider,int preview=0,
  int target=0,int unknown=0)=>new(
   provider,preview+target,target,preview,0,0,0,unknown,
   0,0,0,0,0,0,target,0,target,0,0,0);
 private static VacancyFunnelReport Funnel(
  params VacancyFunnelRow[] sources)=>
  new(Now,0,0,0,FunnelRow("ALL"),sources,"test");

 [Fact]
 public void SeparatesReferenceLossAndRobotaTotalGapsFromOrdinaryRssCap()
 {
  var run=Report(Now,
   Row("dou",2),Row("dou-rss-C#",1),
   Row("robota-api-.net",2,total:5),
   Row("wwr-backend",1));
  var events=new[]
  {
   Event("dou","dou","a"),Event("dou","dou","b"),
   Event("dou","dou-rss-C#","b"),
   Event("robota","robota-api-.net","r1"),
   Event("robota","robota-api-.net","r2"),
   Event("wwr","wwr-backend","w1")
  };
  var result=CoverageActionPlanner.Analyze(
   CoverageReview.Analyze([run],Now),
   DiscoveryGapAudit.Analyze(run,events),Funnel(),Now);
  Assert.Contains(result.Actions,x=>x.Code=="ROBOTA_SEARCH_PAGING_UNVERIFIED" &&
   x.Priority=="P1" && x.Query=="robota-api-.net");
  Assert.Contains(result.Actions,x=>x.Code=="QUERY_ADDS_NO_MARGINAL_URLS" &&
   x.Priority=="P2" && x.Query=="dou-rss-C#");
  Assert.DoesNotContain(result.Actions,x=>x.Query=="wwr-backend" &&
   (x.Code=="SOURCE_FETCH_FAILED"||x.Code=="SOURCE_REFERENCE_LOSS"));
  Assert.DoesNotContain(result.Actions,x=>x.Code=="DISCOVERY_COUNT_MISMATCH");
  Assert.All(result.Actions,x=>Assert.DoesNotContain("https://",x.RecommendedCheck));
 }

 [Fact]
 public void FlagsMissingEventsAndRawLossWithoutConcludingJobClosure()
 {
  var run=Report(Now,Row("djinni",2,dropped:1,
   error:"RSS_REFERENCE_LOSS: unusable=1"));
  var coverage=CoverageReview.Analyze([run],Now);
  var gaps=DiscoveryGapAudit.Analyze(run,[Event("djinni","djinni","1")]);
  var actions=CoverageActionPlanner.Analyze(coverage,gaps,Funnel(),Now).Actions;
  Assert.Contains(actions,x=>x.Code=="DISCOVERY_COUNT_MISMATCH" &&
   x.Priority=="P1");
  Assert.Contains(actions,x=>x.Code=="SOURCE_REFERENCE_LOSS" &&
   x.Priority=="P1");
  Assert.DoesNotContain(actions,x=>x.Code.Contains("CLOSED",StringComparison.Ordinal));
 }

 [Fact]
 public void FailureAndAbsentLatestQueryAreDistinct()
 {
  var newest=Report(Now,
   Row("wwr-backend",0,"FAILED",0,error:"HTTP 403"));
  var older=Report(Now.AddHours(-5),Row("djinni",11));
  var coverage=CoverageReview.Analyze([newest,older],Now);
  var gaps=DiscoveryGapAudit.Analyze(newest,[]);
  var actions=CoverageActionPlanner.Analyze(coverage,gaps,Funnel(),Now).Actions;
  Assert.Contains(actions,x=>x.Code=="SOURCE_FETCH_FAILED" &&
   x.Priority=="P0" && x.Query=="wwr-backend");
  Assert.Contains(actions,x=>x.Code=="QUERY_NOT_IN_LATEST_SCAN" &&
   x.Priority=="P2" && x.Query=="djinni");
  Assert.DoesNotContain(actions,x=>x.Code=="SOURCE_FETCH_FAILED" && x.Query=="djinni");
  Assert.Equal("P0",actions[0].Priority);
 }

 [Fact]
 public void PausedRobotaDetailsAreNotRecommendedForAutomaticRetry()
 {
  var run=Report(Now,Row("robota-api-backend",1,total:1,
   error:"Robota full-text disabled after observed HTTP 403"));
  var actions=CoverageActionPlanner.Analyze(
   CoverageReview.Analyze([run],Now),
   DiscoveryGapAudit.Analyze(run,[Event("robota","robota-api-backend","r1")]),
   Funnel(),Now).Actions;
  var item=Assert.Single(actions,x=>x.Code=="ROBOTA_DETAIL_RESTRICTED");
  Assert.Equal("P2",item.Priority);
  Assert.Contains("paused",item.RecommendedCheck);
  Assert.DoesNotContain(actions,x=>x.Code=="SOURCE_FETCH_FAILED");
 }

 [Fact]
 public void TargetReviewIsProviderLevelNotAttributableToASearchQuery()
 {
  var run=Report(Now,Row("wwr-backend",1));
  var funnel=Funnel(FunnelRow("wwr",preview:2,target:3,unknown:5));
  var plan=CoverageActionPlanner.Analyze(
   CoverageReview.Analyze([run],Now),
   DiscoveryGapAudit.Analyze(run,[Event("wwr","wwr-backend","w1")]),
   funnel,Now);
  Assert.Contains(plan.Actions,x=>x.Code=="REVIEW_TARGET_VACANCIES" &&
   x.Provider=="wwr" && x.Query is null);
  Assert.Contains(plan.Actions,x=>x.Code=="REVIEW_PREVIEW_ONLY" &&
   x.Provider=="wwr" && x.Query is null);
  Assert.DoesNotContain(plan.Actions,x=>x.Code=="REVIEW_TARGET_VACANCIES" &&
   x.Query=="wwr-backend");
 }

 [Fact]
 public void StaleAndTruncatedInputsCannotBePresentedAsCompleteCoverage()
 {
  var old=Report(Now.AddDays(-4),Row("dou",3));
  var funnel=new VacancyFunnelReport(Now,10,500,490,
   FunnelRow("ALL"),[], "partial");
  var plan=CoverageActionPlanner.Analyze(
   CoverageReview.Analyze([old],Now),
   DiscoveryGapAudit.Analyze(old,[
    Event("dou","dou","1"),Event("dou","dou","2"),Event("dou","dou","3")]),
   funnel,Now);
  Assert.Equal(490,plan.VacanciesOmitted);
  Assert.False(plan.CompleteVacancySample);
  Assert.Contains(plan.Actions,x=>x.Code=="STALE_COLLECTOR_EVIDENCE");
  Assert.Contains(plan.Actions,x=>x.Code=="TRUNCATED_VACANCY_AUDIT");
 }

 [Fact]
 public void MissingStoredCrawlsDoNotInventFreshSourceEvidence()
 {
  var plan=CoverageActionPlanner.Analyze(
   CoverageReview.Analyze([],Now),null,Funnel(),Now);
  Assert.Null(plan.LatestCrawlAt);
  Assert.Single(plan.Actions);
  Assert.Equal("NO_SAVED_CRAWL",plan.Actions[0].Code);
 }

 [Fact]
 public void MismatchedLatestRunIsRejectedRatherThanSpliced()
 {
  var first=Report(Now,Row("dou",1));
  var earlier=Report(Now.AddHours(-2),Row("dou",1));
  Assert.Throws<InvalidDataException>(()=>CoverageActionPlanner.Analyze(
   CoverageReview.Analyze([first],Now),
   DiscoveryGapAudit.Analyze(earlier,[Event("dou","dou","1")]),
   Funnel(),Now));
 }
}
