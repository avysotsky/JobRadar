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
 [Fact] public void DjinniFeedExtractsVacancyAndPreview()
 {
  const string xml="""<?xml version="1.0"?><rss version="2.0"><channel><item>
   <title>Middle .NET Developer</title><link>https://djinni.co/jobs/777123-middle-net-developer/?utm_source=rss</link>
   <pubDate>Fri, 09 Oct 2026 09:00:00 GMT</pubDate>
   <description><![CDATA[<p>ASP.NET Core remote position</p>]]></description>
   </item></channel></rss>""";
  var source=new DjinniSource();
  var job=Assert.Single(source.ParseListings(xml));
  Assert.Equal("https://djinni.co/jobs/777123-middle-net-developer/",job.Url);
  Assert.Contains("ASP.NET Core",job.Preview);
  Assert.Equal("Middle .NET Developer",job.Title);
  Assert.NotNull(job.PublishedAt);
  Assert.True(source.IsSinglePageFeed);
 }
 [Fact] public void DjinniFeedReportsInvalidXml()
 {
  Assert.Throws<System.Xml.XmlException>(()=>new DjinniSource().ParseListings("<html>captcha"));
 }
}
