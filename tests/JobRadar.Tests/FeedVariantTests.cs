using JobRadar;
using Xunit;

namespace JobRadar.Tests;
public sealed class FeedVariantTests
{
 [Fact]
 public void ProducesDistinctQueriesWithoutChangingCanonicalJobSource()
 {
  var cfg=new RadarOptions{
   EnabledDou=true,EnabledDjinni=true,
   DouExtraKeywords=["C#","c#","ASP.NET Core"],
   DjinniExtraKeywords=["C#"]
  };
  var feeds=FeedVariants.Create(cfg);
  Assert.Equal(5,feeds.Count);
  Assert.Equal(feeds.Count,feeds.Select(x=>x.Name).Distinct().Count());
  var xml="""<rss><channel><item><title>Middle .NET Engineer</title><link>https://jobs.dou.ua/vacancies/12345/</link></item></channel></rss>""";
  var dou=Assert.IsType<DouKeywordFeed>(feeds.Single(x=>x.Name=="dou-rss-C#"));
  var job=Assert.Single(dou.ParseListings(xml));
  Assert.Equal("dou",job.Source);
  Assert.Contains("search=C%23",dou.ListingUrl(0));
  Assert.Contains("remote=",dou.ListingUrl(0));
  var djinni=Assert.IsType<DjinniKeywordFeed>(feeds.Single(x=>x.Name=="djinni-rss-C#"));
  Assert.Contains("keywords=C%23",djinni.ListingUrl(0));
  Assert.True(dou.IsSinglePageFeed);
  Assert.True(djinni.IsSinglePageFeed);
 }
 [Fact]
 public void DisabledSourcesDoNotCreateFeedVariants()
 {
  var cfg=new RadarOptions{
   EnabledDou=false,EnabledDjinni=false,
   DouExtraKeywords=["C#"],DjinniExtraKeywords=["C#"]};
  Assert.Empty(FeedVariants.Create(cfg));
 }
 [Fact]
 public void EmptyAndOversizedFilterTermsAreIgnored()
 {
  var cfg=new RadarOptions{EnabledDou=true,EnabledDjinni=false,
    DouExtraKeywords=["", "  ",new string('x',61),"  C#  ","c#"]};
  var sources=FeedVariants.Create(cfg);
  Assert.Equal(2,sources.Count);
 }
}
