using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class QueryYieldTrendTests
{
 private static readonly DateTimeOffset Now=
  new(2026,10,13,16,0,0,TimeSpan.Zero);
 private static readonly string Query="dou-rss-C#";

 private static QueryActionableYieldRow Row(string query="dou-rss-C#",
  string provider="dou",int? marginal=2,int? target=1)=>new(
   query,provider,marginal??0,marginal??0,marginal,
   marginal.HasValue?0:null,target,marginal.HasValue?0:null,
   marginal.HasValue?0:null,marginal.HasValue?0:null,
   marginal.HasValue?0:null,[]);

 private static QueryActionableYieldReport Run(
  int daysBefore,int? marginal,int? target,
  QueryActionableYieldRow[]? queries=null,long omitted=0)=>
  new(Now.AddDays(-daysBefore).AddMinutes(-10),
   Now.AddDays(-daysBefore),Now,
   10+omitted,10,omitted,queries??[Row(marginal:marginal,target:target)],
   new Dictionary<string,int?>(),"current stored assessment");

 [Fact]
 public void ThreeCleanRunsWithTargetYieldRecommendHumanRetentionReview()
 {
  var trend=QueryYieldTrend.Analyze([
   Run(0,3,2),Run(1,1,1),Run(2,2,0)
  ],Now);
  var row=Assert.Single(trend.Queries);
  Assert.Equal(Query,row.Query);
  Assert.Equal(3,row.VerifiedRuns);
  Assert.Equal(6,row.SumMarginalUrls);
  Assert.Equal(3,row.SumCurrentTargetBackendReviews);
  Assert.Equal(3,row.RunsWithNewUrls);
  Assert.Equal(2,row.RunsWithTargetReviews);
  Assert.Equal("REVIEW_RETAIN_TARGET_QUERY",row.Assessment);
  Assert.Empty(row.Signals);
 }

 [Fact]
 public void RepeatedZeroMarginalResultsDoNotBecomeProviderWideZero()
 {
  var trend=QueryYieldTrend.Analyze([
   Run(0,0,0),Run(1,0,0),Run(2,0,0)
  ],Now);
  var row=Assert.Single(trend.Queries);
  Assert.Equal(0,row.SumMarginalUrls);
  Assert.Equal("REVIEW_ZERO_MARGINAL_QUERY",row.Assessment);
  Assert.Contains("RSS caps",trend.Interpretation);
 }

 [Fact]
 public void ValidNewUrlsWithoutTargetFitAreNotSilentlyDisabled()
 {
  var row=Assert.Single(QueryYieldTrend.Analyze([
   Run(0,2,0),Run(1,3,0),Run(2,1,0)
  ],Now).Queries);
  Assert.Equal(6,row.SumMarginalUrls);
  Assert.Equal(0,row.SumCurrentTargetBackendReviews);
  Assert.Equal("REVIEW_LOW_TARGET_YIELD",row.Assessment);
 }

 [Fact]
 public void TwoRunsRemainInsufficientAndTotalsAreNotPublished()
 {
  var row=Assert.Single(QueryYieldTrend.Analyze([
   Run(0,2,1),Run(1,2,1)
  ],Now).Queries);
  Assert.Equal("INSUFFICIENT_RUNS",row.Assessment);
  Assert.Equal(2,row.VerifiedRuns);
  Assert.Null(row.SumMarginalUrls);
  Assert.Null(row.SumCurrentTargetBackendReviews);
  Assert.Contains("NEED_AT_LEAST_THREE_RUNS",row.Signals);
 }

 [Fact]
 public void MissingOrReorderedQueryInvalidatesComparisonNotAZero()
 {
  var first=Row("dou");
  var second=Row();
  var reports=new[]{
   Run(0,0,0,queries:[first,second]),
   Run(1,0,0,queries:[second,first]),
   Run(2,0,0,queries:[first,second])
  };
  var trend=QueryYieldTrend.Analyze(reports,Now);
  Assert.Equal(2,trend.Queries.Count);
  Assert.All(trend.Queries,x=>
  {
   Assert.Equal("UNVERIFIED",x.Assessment);
   Assert.Null(x.SumMarginalUrls);
   Assert.Contains("QUERY_ORDER_OR_SET_CHANGED",x.Signals);
  });
  var missing=QueryYieldTrend.Analyze([
   Run(0,0,0,queries:[first,second]),
   Run(1,0,0,queries:[first]),
   Run(2,0,0,queries:[first,second])
  ],Now);
  Assert.All(missing.Queries,x=>Assert.Equal("UNVERIFIED",x.Assessment));
 }

 [Fact]
 public void TruncatedOrUnverifiedRunsPoisonOnlyTheAffectedProvider()
 {
  var dou=Row(marginal:1,target:1);
  var wwr=Row("wwr-backend","wwr",1,1);
  var unknown=Row(marginal:null,target:null);
  var trend=QueryYieldTrend.Analyze([
   Run(0,0,0,queries:[dou,wwr]),
   Run(1,0,0,queries:[unknown,wwr]),
   Run(2,0,0,queries:[dou,wwr])
  ],Now);
  Assert.Equal("UNVERIFIED",Assert.Single(trend.Queries,x=>x.Provider=="dou").Assessment);
  Assert.Equal("REVIEW_RETAIN_TARGET_QUERY",
   Assert.Single(trend.Queries,x=>x.Provider=="wwr").Assessment);

  var truncation=QueryYieldTrend.Analyze([
   Run(0,1,1),Run(1,1,1,omitted:20),Run(2,1,1)
  ],Now);
  var row=Assert.Single(truncation.Queries);
  Assert.Equal("UNVERIFIED",row.Assessment);
  Assert.Contains("TRUNCATED_CURRENT_SNAPSHOT",row.Signals);
  Assert.Null(row.RunsWithNewUrls);
 }

 [Fact]
 public void OverlappingWindowsAreNotCountedAsIndependentEvidence()
 {
  // Same run time means the inclusive event windows necessarily overlap.
  var row=Assert.Single(QueryYieldTrend.Analyze([
   Run(0,1,1),Run(0,1,1),Run(1,1,1)
  ],Now).Queries);
  Assert.Equal("UNVERIFIED",row.Assessment);
  Assert.Contains("OVERLAPPING_CRAWL_WINDOWS",row.Signals);
  Assert.Null(row.RunsWithTargetReviews);
 }

 [Fact]
 public void SnapshotIncompletenessAndLowTargetSampleStayVisible()
 {
  var results=QueryYieldTrend.Analyze([
   Run(0,1,1),Run(1,1,0),Run(2,1,0)
  ],Now);
  var row=Assert.Single(results.Queries);
  Assert.Equal("MIXED_TARGET_YIELD",row.Assessment);
  Assert.Equal(1,row.RunsWithTargetReviews);
 }

 [Fact]
 public void RejectMalformedSequenceDuplicateQueriesAndUnboundedHistory()
 {
  Assert.Throws<InvalidDataException>(()=>QueryYieldTrend.Analyze([
   Run(2,1,1),Run(0,1,1)
  ],Now));
  Assert.Throws<InvalidDataException>(()=>QueryYieldTrend.Analyze([
   Run(0,1,1,queries:[Row(),Row("DOU-RSS-C#")])
  ],Now));
  Assert.Throws<ArgumentOutOfRangeException>(()=>QueryYieldTrend.Analyze(
   Enumerable.Repeat(Run(0,1,1),11).ToArray(),Now));
  var empty=QueryYieldTrend.Analyze([],Now);
  Assert.Empty(empty.Queries);
  Assert.Equal(0,empty.RunsExamined);
  Assert.Null(empty.NewestRunEndedAt);
 }
}
