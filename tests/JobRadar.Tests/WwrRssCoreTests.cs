using System.Net;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class WwrRssCoreTests
{
 private static string Rss(string url)=>$"""
<?xml version="1.0" encoding="utf-8"?>
<rss version="2.0"><channel>
 <item><title>Middle .NET Backend Engineer</title>
 <link>{url}</link>
 <pubDate>Tue, 13 Oct 2026 08:00:00 GMT</pubDate>
 <description><![CDATA[<p>ASP.NET Core, EF Core, PostgreSQL, remote work</p>]]></description></item>
</channel></rss>
""";

 [Fact]
 public void TwoBoundedWwrQueriesRetainOneCanonicalProviderAndUniqueIdentity()
 {
  var first=new WwrRssSource("backend");
  var second=new WwrRssSource("programming");
  Assert.True(first.IsSinglePageFeed);
  Assert.True(second.IsSinglePageFeed);
  Assert.Equal(RemoteBoards.WwrBackend,first.ListingUrl(0));
  Assert.Equal(RemoteBoards.WwrProgramming,second.ListingUrl(0));
  Assert.Throws<NotSupportedException>(()=>first.ListingUrl(1));

  var url="https://weworkremotely.com/remote-jobs/middle-dotnet-20261013";
  var firstJob=Assert.Single(first.ParseFeed(Rss(url)).Jobs);
  var secondJob=Assert.Single(second.ParseFeed(Rss(url)).Jobs);
  Assert.Equal("wwr",firstJob.Source);
  Assert.Equal(firstJob.Url,secondJob.Url);
  Assert.Equal(url,firstJob.Url);
  Assert.Contains("ASP.NET Core",firstJob.Preview);
  Assert.Equal("wwr-backend",first.Name);
  Assert.Equal("wwr-programming",second.Name);
 }

 [Fact]
 public void ProviderLinksAndDuplicatesAreAuditedNotSilentlyLost()
 {
  var url="https://weworkremotely.com/remote-jobs/wwr-dotnet-100";
  var duplicated=Rss(url);
  var one=duplicated.Substring(duplicated.IndexOf("<item>",StringComparison.Ordinal),
   duplicated.IndexOf("</item>",StringComparison.Ordinal)-duplicated.IndexOf("<item>",StringComparison.Ordinal)+7);
  var rss=duplicated.Replace("</channel>",one+"<item><title>Invalid host</title>"+
   "<link>https://another.invalid/remote-jobs/wwr-dotnet-100</link></item></channel>");
  var result=new WwrRssSource("backend").ParseFeed(rss);
  Assert.Equal(3,result.RawItems);
  Assert.Equal(1,result.UnusableItems);
  Assert.Equal(1,result.DuplicateItems);
  Assert.Single(result.Jobs);
 }

 [Fact]
 public async Task WwrRssAddsProvenanceButDoesNotFetchVacancyDetailOrAssertOpen()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var url="https://weworkremotely.com/remote-jobs/"+Guid.NewGuid().ToString("N");
  var feed=Rss(url);
  int rssRequests=0,detailRequests=0;
  using var client=new HttpClient(new Handler(req=>
  {
   if(req.RequestUri?.AbsolutePath.EndsWith(".rss",StringComparison.Ordinal)==true)
   {
    rssRequests++;
    return new HttpResponseMessage(HttpStatusCode.OK)
     {Content=new StringContent(feed)};
   }
   detailRequests++;
   return new HttpResponseMessage(HttpStatusCode.NotFound);
  }));
  var collector=new Crawler(new HttpFetcher(client,0),storage,
   new RadarOptions{RetryAfterCrawl=false});
  var report=await collector.RunAsync([
   new WwrRssSource("backend"),new WwrRssSource("programming")],
   CancellationToken.None);
  Assert.Equal(2,rssRequests);
  Assert.Equal(0,detailRequests);
  Assert.Equal(2,report.Sources.Count);
  Assert.All(report.Sources,s=>{
   Assert.Equal("PARTIAL",s.Status);
   Assert.Equal(1,s.ReferencesFound);
   Assert.Equal(1,s.RawRecords);
   Assert.Equal(0,s.DroppedRecords);
   Assert.Equal(0,s.DetailsFetched);
   Assert.Equal(0,s.DetailsFailed);
  });
  var all=await storage.ReadVacanciesForTriageAsync(50000,CancellationToken.None);
  var stored=Assert.Single(all,x=>x.Url==url);
  Assert.Equal("wwr",stored.Source);
  Assert.False(stored.HasFullText);
  Assert.Null(stored.IsOpen);
  Assert.Contains("wwr-backend",stored.Queries);
  Assert.Contains("wwr-programming",stored.Queries);
  var triaged=VacancyTriage.Assess(stored);
  Assert.Equal(FitBucket.NeedsReview,triaged.Bucket);
  Assert.Contains(triaged.Warnings,w=>w.Contains("Украины"));
 }

 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):
  HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,CancellationToken ct)=>
   Task.FromResult(respond(request));
 }
}
