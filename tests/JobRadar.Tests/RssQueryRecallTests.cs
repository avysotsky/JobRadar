using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class RssQueryRecallTests
{
 private static readonly DateTimeOffset Now=
  new(2026,10,13,9,0,0,TimeSpan.Zero);

 private static CrawlReport Report(params SourceResult[] sources)=>
  new(Now.AddMinutes(-2),Now,sources);

 private static SourceResult Row(string name,int count,string status="PARTIAL")=>
  new(name,1,count,0,0,null,status,null,RawRecords:count,DroppedRecords:0);

 private static RssQueryObservation Found(string provider,string query,string url)=>
  new(provider,query,url);

 [Fact]
 public void CountsIncrementalReferencesWithinEachCanonicalProvider()
 {
  var report=Report(Row("dou",2),Row("dou-rss-C#",2),
   Row("dou-rss-ASP.NET Core",2),Row("djinni",1),Row("djinni-rss-C#",1));
  var data=new[]
  {
   Found("dou","dou","https://jobs.dou.ua/vacancies/1/"),
   Found("dou","dou","https://jobs.dou.ua/vacancies/2/"),
   Found("dou","dou-rss-C#","https://jobs.dou.ua/vacancies/2/"),
   Found("dou","dou-rss-C#","https://jobs.dou.ua/vacancies/3/"),
   Found("dou","dou-rss-ASP.NET Core","https://jobs.dou.ua/vacancies/3/"),
   Found("dou","dou-rss-ASP.NET Core","https://jobs.dou.ua/vacancies/4/"),
   Found("djinni","djinni","https://djinni.co/jobs/11-net/"),
   Found("djinni","djinni-rss-C#","https://djinni.co/jobs/11-net/")
  };
  var review=RssQueryRecall.Analyze(report,data);
  Assert.Equal(5,review.Queries.Count);
  Assert.Equal(new int?[]{2,1,1,1,0},
   review.Queries.Select(x=>x.IncrementalUrls).ToArray());
  Assert.Equal(new int?[]{0,1,1,0,1},
   review.Queries.Select(x=>x.AlreadySeenInEarlierQueries).ToArray());
  Assert.Equal(4,review.IncrementalProviderTotals["dou"]);
  Assert.Equal(1,review.IncrementalProviderTotals["djinni"]);
  Assert.All(review.Queries,x=>Assert.True(x.MatchesSavedCrawl));
 }

 [Fact]
 public void DuplicateEventsWithinOneQueryDoNotIncreaseObservedUniqueUrls()
 {
  var report=Report(Row("dou-rss-C#",1));
  var observations=new[]
  {
   Found("dou","dou-rss-C#","https://jobs.dou.ua/vacancies/1/"),
   Found("dou","dou-rss-C#","https://jobs.dou.ua/vacancies/1/")
  };
  var one=Assert.Single(RssQueryRecall.Analyze(report,observations).Queries);
  Assert.Equal(1,one.ObservedDistinctUrls);
  Assert.Equal(1,one.IncrementalUrls);
  Assert.True(one.MatchesSavedCrawl);
 }

 [Fact]
 public void MismatchedPriorQueryInvalidatesLaterIncrementalAttribution()
 {
  var report=Report(Row("dou",2),Row("dou-rss-C#",1));
  var observations=new[]
  {
   Found("dou","dou","https://jobs.dou.ua/vacancies/1/"),
   Found("dou","dou-rss-C#","https://jobs.dou.ua/vacancies/2/")
  };
  var review=RssQueryRecall.Analyze(report,observations);
  Assert.False(review.Queries[0].MatchesSavedCrawl);
  Assert.True(review.Queries[1].MatchesSavedCrawl);
  Assert.Null(review.Queries[0].IncrementalUrls);
  Assert.Null(review.Queries[1].IncrementalUrls);
  Assert.Null(review.IncrementalProviderTotals["dou"]);
  Assert.Contains("REPORT_EVENT_COUNT_MISMATCH",review.Queries[0].Signals);
  Assert.Contains("INCREMENTAL_ATTRIBUTION_UNVERIFIED",review.Queries[1].Signals);
 }

 [Fact]
 public void FailedQueryCannotProduceVerifiedZeroOrTaintOtherProvider()
 {
  var review=RssQueryRecall.Analyze(
   Report(Row("dou",0,"FAILED"),Row("djinni",0)),
   Array.Empty<RssQueryObservation>());
  var failed=review.Queries[0];
  Assert.False(failed.MatchesSavedCrawl);
  Assert.Null(failed.IncrementalUrls);
  Assert.Contains("RSS_SOURCE_FAILED",failed.Signals);
  Assert.Null(review.IncrementalProviderTotals["dou"]);
  var empty=review.Queries[1];
  Assert.True(empty.MatchesSavedCrawl);
  Assert.Equal(0,empty.IncrementalUrls);
  Assert.Equal(0,review.IncrementalProviderTotals["djinni"]);
 }

 [Fact]
 public void IgnoresNonRssSourcesAndOtherProviderEvents()
 {
  var report=Report(Row("robota-api-backend",15),Row("dou",1));
  var observations=new[]
  {
   Found("robota","robota-api-backend","https://robota.ua/123"),
   Found("djinni","dou","https://jobs.dou.ua/vacancies/9/"),
   Found("dou","dou-rss-unconfigured","https://jobs.dou.ua/vacancies/9/"),
   Found("dou","dou","https://jobs.dou.ua/vacancies/1/")
  };
  var row=Assert.Single(RssQueryRecall.Analyze(report,observations).Queries);
  Assert.Equal("dou",row.Query);
  Assert.Equal(1,row.ObservedDistinctUrls);
  Assert.True(row.MatchesSavedCrawl);
 }

 [Fact]
 public void EmptyHistoryDoesNotClaimSiteWideZeroOrInventQueries()
 {
  var result=RssQueryRecall.Analyze(Report(),[]);
  Assert.Empty(result.Queries);
  Assert.Empty(result.IncrementalProviderTotals);
 }

 [Fact]
 public void FeedVariantsIncludeNewKeywordAndKeepCanonicalJobIdentity()
 {
  var feeds=FeedVariants.Create(new RadarOptions
  {
   EnabledDou=true,EnabledDjinni=true,
   DouExtraKeywords=["C#","ASP.NET Core"],
   DjinniExtraKeywords=["C#","ASP.NET Core"]
  });
  Assert.Equal(6,feeds.Count);
  var dou=Assert.Single(feeds.OfType<DouKeywordFeed>()
   .Where(x=>x.Name=="dou-rss-ASP.NET Core"));
  var djinni=Assert.Single(feeds.OfType<DjinniKeywordFeed>()
   .Where(x=>x.Name=="djinni-rss-ASP.NET Core"));
  Assert.Contains("search=ASP.NET%20Core",dou.ListingUrl(0));
  Assert.Contains("keywords=ASP.NET%20Core",djinni.ListingUrl(0));
  var douXml="""<rss><channel><item><title>ASP.NET Core Backend</title><link>https://jobs.dou.ua/vacancies/1234/</link></item></channel></rss>""";
  Assert.Equal("dou",Assert.Single(dou.ParseListings(douXml)).Source);
  var djinniXml="""<rss><channel><item><title>ASP.NET Core Backend</title><link>https://djinni.co/jobs/1234-backend/</link></item></channel></rss>""";
  Assert.Equal("djinni",Assert.Single(djinni.ParseListings(djinniXml)).Source);
 }
}
