using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class CoverageReviewTests
{
 private static readonly DateTimeOffset Now=
  new(2026,10,12,16,0,0,TimeSpan.Zero);

 private static SourceResult Row(
  string name,int references=1,string status="PARTIAL",
  int? total=null,int? raw=null,int? dropped=null,string? error=null,
  int detailsFailed=0)=>
  new(name,1,references,0,detailsFailed,total,status,error,
   RawRecords:raw,DroppedRecords:dropped);

 private static CrawlReport Run(DateTimeOffset ended,params SourceResult[] rows)=>
  new(ended.AddMinutes(-1),ended,rows);

 [Fact]
 public void NoSavedCrawlsOrNonTargetSourcesNeverInventCoverage()
 {
  var empty=CoverageReview.Analyze([],Now);
  Assert.Equal(0,empty.ReportsExamined);
  Assert.Null(empty.NewestReportAt);
  Assert.Empty(empty.Sources);
  var unrelated=CoverageReview.Analyze(
   [Run(Now,Row("workua-c-sharp",0,"FAILED"))],Now);
  Assert.Equal(1,unrelated.ReportsExamined);
  Assert.Empty(unrelated.Sources);
 }

 [Fact]
 public void ClassifiesTheLatestRobotaLossSeparatelyFromNormalCappedRss()
 {
  var rows=new[]
  {
   Row("robota-api-.net",4,total:10,raw:5,dropped:1,
    error:"Robota API search pages overlap: repeated vacancy IDs across pages"),
   Row("dou-rss-C#",25,raw:25,dropped:0),
   Row("djinni-rss-C#",10,raw:13,dropped:3,
    error:"RSS_REFERENCE_LOSS: unusable=2, duplicates=1")
  };
  var review=CoverageReview.Analyze([Run(Now,rows)],Now);
  Assert.Equal(3,review.Sources.Count);
  var robota=Assert.Single(review.Sources,x=>x.Provider=="Robota");
  Assert.Equal("P1",robota.Priority);
  Assert.Equal(6,robota.UnobservedFromFirstTotal);
  Assert.Contains(RobotaCoverageSignals.PageOverlap,robota.Signals);
  Assert.Contains("RAW_ITEMS_DISCARDED",robota.Signals);
  var dou=Assert.Single(review.Sources,x=>x.Provider=="DOU");
  Assert.Equal("P2",dou.Priority); // RSS cap alone isn't a scanner failure
  Assert.Contains("RSS_NOT_EXHAUSTIVE",dou.Signals);
  Assert.DoesNotContain("LATEST_SCAN_FAILED",dou.Signals);
  var djinni=Assert.Single(review.Sources,x=>x.Provider=="Djinni");
  Assert.Equal("P1",djinni.Priority);
  Assert.Contains("RSS_REFERENCE_LOSS",djinni.Signals);
  Assert.Null(djinni.UnobservedFromFirstTotal);
  Assert.Equal("P1",review.Sources[0].Priority);
 }

 [Fact]
 public void MissingFromNewestRunMeansNotScannedNotZeroVacancies()
 {
  var latest=Run(Now,Row("djinni",20,raw:20,dropped:0));
  var prior=Run(Now.AddHours(-3),Row("dou",30,raw:30,dropped:0));
  var review=CoverageReview.Analyze([latest,prior],Now);
  var dou=Assert.Single(review.Sources,x=>x.Source=="dou");
  Assert.False(dou.PresentInNewestRun);
  Assert.Equal(30,dou.LatestReferences);
  Assert.Equal(1,dou.RunsObserved);
  Assert.Equal("P2",dou.Priority);
  Assert.Contains("NOT_IN_NEWEST_RUN",dou.Signals);
  Assert.DoesNotContain("LATEST_SCAN_FAILED",dou.Signals);
  Assert.True(Assert.Single(review.Sources,x=>x.Source=="djinni").PresentInNewestRun);
 }

 [Fact]
 public void RepeatedQueriesWithinOneReportDoNotInflateHistory()
 {
  var duplicated=Run(Now,Row("dou",20),Row("dou",0,"FAILED"));
  var result=CoverageReview.Analyze([duplicated],Now);
  var item=Assert.Single(result.Sources);
  Assert.Equal(1,item.RunsObserved);
  Assert.Equal(20,item.LatestReferences);
  Assert.Equal("P2",item.Priority);
 }

 [Fact]
 public void HalfSizeDeclineUsesOnlyPriorObservedNonFailedBaselines()
 {
  var reports=new[]
  {
   Run(Now,Row("robota-api-backend",8,total:8,raw:8,dropped:0,
    status:"QUERY_RECONCILED")),
   Run(Now.AddDays(-1),Row("robota-api-backend",0,status:"FAILED")),
   Run(Now.AddDays(-2),Row("robota-api-backend",30,total:30,raw:30,
    dropped:0,status:"QUERY_RECONCILED")),
   Run(Now.AddDays(-3),Row("robota-api-backend",20,total:20,raw:20,
    dropped:0,status:"QUERY_RECONCILED"))
  };
  var result=Assert.Single(CoverageReview.Analyze(reports,Now).Sources);
  Assert.Equal("P1",result.Priority);
  Assert.Equal(30,result.PriorObservedPeak);
  Assert.Equal(4,result.RunsObserved);
  Assert.Contains("OBSERVED_REFERENCES_DOWN_OVER_50_PERCENT",result.Signals);
  Assert.Equal(0,result.UnobservedFromFirstTotal);
 }

 [Fact]
 public void SmallHistoricalCountsDoNotTriggerNoisyDeclineAlert()
 {
  var result=Assert.Single(CoverageReview.Analyze([
   Run(Now,Row("robota-api-.net",1,total:1,raw:1,dropped:0,
    status:"QUERY_RECONCILED")),
   Run(Now.AddHours(-4),Row("robota-api-.net",9,total:9,raw:9,
    dropped:0,status:"QUERY_RECONCILED"))
  ],Now).Sources);
  Assert.Equal(9,result.PriorObservedPeak);
  Assert.Equal("P3",result.Priority);
  Assert.DoesNotContain("OBSERVED_REFERENCES_DOWN_OVER_50_PERCENT",result.Signals);
 }

 [Fact]
 public void ProviderConfirmedZeroAndMissingRawCountsRemainDistinct()
 {
  var confirmed=Row("robota-api-empty",0,"QUERY_RECONCILED",
   total:0,raw:0,dropped:0);
  var historical=Row("robota-api-older",0,"QUERY_RECONCILED",total:0);
  var review=CoverageReview.Analyze([Run(Now,confirmed,historical)],Now);
  var good=Assert.Single(review.Sources,x=>x.Source=="robota-api-empty");
  Assert.Equal("P3",good.Priority);
  Assert.Empty(good.Signals);
  var old=Assert.Single(review.Sources,x=>x.Source=="robota-api-older");
  Assert.Null(old.LatestRawRecords);
  Assert.DoesNotContain(RobotaCoverageSignals.VerifiedZero,old.Signals);
 }

 [Fact]
 public void FailedFetchAndStaleSourceRankAheadOfRoutinePartial()
 {
  var current=Run(Now,
   Row("dou",0,"FAILED",error:"HTTP 403"),
   Row("djinni",7,raw:7,dropped:0));
  var history=Run(Now.AddDays(-4),
   Row("robota-api-old",11,"QUERY_RECONCILED",total:11,raw:11,dropped:0));
  var review=CoverageReview.Analyze([current,history],Now);
  var failed=Assert.Single(review.Sources,x=>x.Source=="dou");
  Assert.Equal("P0",failed.Priority);
  Assert.Contains("LATEST_SCAN_FAILED",failed.Signals);
  Assert.Null(failed.LatestRawRecords);
  var stale=Assert.Single(review.Sources,x=>x.Source=="robota-api-old");
  Assert.Equal("P2",stale.Priority);
  Assert.Contains("LAST_OBSERVATION_OVER_48H",stale.Signals);
  Assert.Contains("NOT_IN_NEWEST_RUN",stale.Signals);
  Assert.Equal("dou",review.Sources[0].Source);
 }

 [Fact]
 public void RobotaTotalDriftAndFailedDetailsAreActionable()
 {
  var review=CoverageReview.Analyze([Run(Now,
   Row("robota-api-total",2,total:2,raw:2,dropped:0,
    error:"REPORTED_TOTAL_DRIFT: changed"),
   Row("robota-api-detail",3,total:3,raw:3,dropped:0,detailsFailed:2)
  )],Now);
  Assert.All(review.Sources,item=>Assert.Equal("P1",item.Priority));
  Assert.Contains(RobotaCoverageSignals.TotalDrift,
   Assert.Single(review.Sources,x=>x.Source=="robota-api-total").Signals);
  Assert.Contains("DETAIL_FETCH_FAILURES",
   Assert.Single(review.Sources,x=>x.Source=="robota-api-detail").Signals);
 }
}
