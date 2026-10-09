using JobRadar;
using Xunit;
namespace JobRadar.Tests;
public sealed class SourceTests
{
 [Fact] public void DouFeedReadsLinksAndDates()
 {
  const string xml="""<?xml version="1.0"?><rss version="2.0"><channel><item><title>Middle .NET Developer</title><link>https://jobs.dou.ua/vacancies/12345/</link><pubDate>Fri, 09 Oct 2026 09:00:00 GMT</pubDate></item></channel></rss>""";
  var source=new DouSource();
  var items=source.ParseListings(xml);
  Assert.Single(items);
  Assert.Equal("Middle .NET Developer",items[0].Title);
  Assert.NotNull(items[0].PublishedAt);
  Assert.True(source.IsSinglePageFeed);
 }
 [Fact] public void DouFeedFailsClosedOnInvalidXml()=>Assert.Throws<System.Xml.XmlException>(()=>new DouSource().ParseListings("<html>blocked"));
 [Fact] public void DjinniUsesExplicitPageNumbers()
 {
  var source=new DjinniSource();
  Assert.Contains("page=1",source.ListingUrl(0));
  Assert.Contains("page=2",source.ListingUrl(1));
 }
}