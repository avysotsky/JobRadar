using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class DiscoveryGapAuditTests
{
 private static readonly DateTimeOffset Now=
  new(2026,10,13,12,0,0,TimeSpan.Zero);

 private static SourceResult Row(string query,int references,
  string status="PARTIAL",int? total=null,int? dropped=0,
  string? error=null,int pages=1,int failedDetails=0)=>
  new(query,pages,references,0,failedDetails,total,status,error,
   RawRecords:references,DroppedRecords:dropped);

 private static CrawlReport Report(params SourceResult[] rows)=>
  new(Now.AddMinutes(-1),Now,rows);

 private static DiscoveryGapObservation Event(string provider,string query,string url)=>
  new(provider,query,url);

 [Fact]
 public void ProviderQueryNamesAreStrictlyScopedToFourEnabledFamilies()
 {
  Assert.Equal("dou",DiscoveryGapAudit.ProviderForQuery("dou-rss-ASP.NET Core"));
  Assert.Equal("djinni",DiscoveryGapAudit.ProviderForQuery("djinni-rss-C#"));
  Assert.Equal("robota",DiscoveryGapAudit.ProviderForQuery("robota-api-backend"));
  Assert.Equal("wwr",DiscoveryGapAudit.ProviderForQuery("wwr-backend"));
  Assert.Equal("wwr",DiscoveryGapAudit.ProviderForQuery("wwr-programming"));
  Assert.Null(DiscoveryGapAudit.ProviderForQuery("wwr"));
  Assert.Null(DiscoveryGapAudit.ProviderForQuery("wwr-front-end"));
  Assert.Null(DiscoveryGapAudit.ProviderForQuery("robota-api-"));
  Assert.Null(DiscoveryGapAudit.ProviderForQuery("workua-c#"));
  Assert.Null(DiscoveryGapAudit.ProviderForQuery("jooble"));
 }

 [Fact]
 public void MeasuresWithinRunMarginalReferencesAcrossFourProviders()
 {
  var report=Report(
   Row("dou",2),Row("dou-rss-C#",2),
   Row("djinni",1),
   Row("robota-api-.net",2,total:4),
   Row("wwr-backend",2),Row("wwr-programming",2));
  var evidence=new[]
  {
   Event("dou","dou","dou://1"),Event("dou","dou","dou://2"),
   Event("dou","dou-rss-C#","dou://2"),
   Event("dou","dou-rss-C#","dou://3"),
   Event("djinni","djinni","djinni://1"),
   Event("robota","robota-api-.net","robota://1"),
   Event("robota","robota-api-.net","robota://2"),
   Event("wwr","wwr-backend","wwr://1"),
   Event("wwr","wwr-backend","wwr://2"),
   Event("wwr","wwr-programming","wwr://2"),
   Event("wwr","wwr-programming","wwr://3"),
   Event("robota","wwr-programming","robota://contamination")
  };
  var result=DiscoveryGapAudit.Analyze(report,evidence);
  Assert.Equal(6,result.Queries.Count);
  Assert.Equal(3,result.PerProviderObservedUniqueUrls["dou"]);
  Assert.Equal(1,result.PerProviderObservedUniqueUrls["djinni"]);
  Assert.Equal(2,result.PerProviderObservedUniqueUrls["robota"]);
  Assert.Equal(3,result.PerProviderObservedUniqueUrls["wwr"]);
  var secondDou=Assert.Single(result.Queries,x=>x.Query=="dou-rss-C#");
  Assert.Equal(1,secondDou.NewToEarlierQueries);
  Assert.Equal(1,secondDou.OverlapWithEarlierQueries);
  var programming=Assert.Single(result.Queries,x=>x.Query=="wwr-programming");
  Assert.True(programming.MatchesStoredDiscoveries);
  Assert.Equal(1,programming.NewToEarlierQueries);
  Assert.Equal(1,programming.OverlapWithEarlierQueries);
  Assert.Contains("RSS_NOT_EXHAUSTIVE",programming.Signals);
  var robota=Assert.Single(result.Queries,x=>x.Query=="robota-api-.net");
  Assert.Equal("P1",robota.Priority);
  Assert.Equal(2,robota.UnobservedFromReportedTotal);
  Assert.Contains("UNOBSERVED_FROM_REPORTED_TOTAL",robota.Signals);
  Assert.Equal("P1",result.Queries[0].Priority);
 }

 [Fact]
 public void MismatchedDiscoveryEventsTaintOnlyThatProvider()
 {
  var audit=DiscoveryGapAudit.Analyze(Report(
   Row("wwr-backend",2),Row("wwr-programming",1),
   Row("dou",1)),[
   Event("wwr","wwr-backend","wwr://1"),
   Event("wwr","wwr-programming","wwr://2"),
   Event("dou","dou","dou://1")
  ]);
  var first=Assert.Single(audit.Queries,x=>x.Query=="wwr-backend");
  var second=Assert.Single(audit.Queries,x=>x.Query=="wwr-programming");
  var dou=Assert.Single(audit.Queries,x=>x.Query=="dou");
  Assert.False(first.MatchesStoredDiscoveries);
  Assert.Null(first.NewToEarlierQueries);
  Assert.True(second.MatchesStoredDiscoveries);
  Assert.Null(second.NewToEarlierQueries);
  Assert.Equal("P1",first.Priority);
  Assert.Null(audit.PerProviderObservedUniqueUrls["wwr"]);
  Assert.Equal(1,audit.PerProviderObservedUniqueUrls["dou"]);
  Assert.Equal(1,dou.NewToEarlierQueries);
 }

 [Fact]
 public void FailureAndRawLossCannotBeMistakenForCleanEmptyFeeds()
 {
  var audit=DiscoveryGapAudit.Analyze(Report(
   Row("wwr-backend",0,"FAILED",pages:0),
   Row("djinni",0,error:"RSS_EMPTY_UNVERIFIED"),
   Row("dou",1,dropped:2,error:"RSS_REFERENCE_LOSS")),[
   Event("dou","dou","dou://1")
  ]);
  Assert.Equal("P0",Assert.Single(audit.Queries,x=>x.Query=="wwr-backend").Priority);
  Assert.Equal("P1",Assert.Single(audit.Queries,x=>x.Query=="djinni").Priority);
  Assert.Equal("P1",Assert.Single(audit.Queries,x=>x.Query=="dou").Priority);
  Assert.Null(audit.PerProviderObservedUniqueUrls["wwr"]);
  Assert.Contains("RAW_RECORDS_DROPPED",
   Assert.Single(audit.Queries,x=>x.Query=="dou").Signals);
 }

 [Fact]
 public void DuplicateQueryNamesAndInvalidWindowsFailClosed()
 {
  Assert.Throws<InvalidDataException>(()=>DiscoveryGapAudit.Analyze(
   Report(Row("wwr-backend",1),Row("WWR-BACKEND",2)),[]));
  Assert.Throws<InvalidDataException>(()=>DiscoveryGapAudit.Analyze(
   new CrawlReport(Now,Now.AddHours(-1),[]),[]));
  var empty=DiscoveryGapAudit.Analyze(Report(),[]);
  Assert.Empty(empty.Queries);
  Assert.Empty(empty.PerProviderObservedUniqueUrls);
 }

 [Fact]
 public void SavedCoverageReviewIncludesWwrButDoesNotCertifyExhaustiveFeed()
 {
  var review=CoverageReview.Analyze([
   Report(Row("wwr-backend",10),Row("wwr-programming",8))
  ],Now);
  Assert.Equal(2,review.Sources.Count);
  Assert.All(review.Sources,x=>
  {
   Assert.Equal("WWR",x.Provider);
   Assert.Equal("P2",x.Priority);
   Assert.Contains("RSS_NOT_EXHAUSTIVE",x.Signals);
  });
 }
}
