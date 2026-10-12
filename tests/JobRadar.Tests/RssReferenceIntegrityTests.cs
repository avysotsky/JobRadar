using System.Net;
using System.Text.Json;
using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

public sealed class RssReferenceIntegrityTests
{
 [Theory]
 [InlineData("dou")]
 [InlineData("dou-keyword")]
 [InlineData("djinni")]
 [InlineData("djinni-keyword")]
 public void ParserAccountsForEveryItemAndKeepsCanonicalSources(string kind)
 {
  var source=CreateSource(kind);
  var data=Fixture(kind);
  var rss=Assert.IsAssignableFrom<IRssFeedSource>(source);
  var page=rss.ParseFeed(data.Xml);
  Assert.Equal(4,page.RawItems);
  Assert.Equal(2,page.UnusableItems);
  Assert.Equal(1,page.DuplicateItems);
  Assert.Equal(page.RawItems,page.Jobs.Count+page.DiscardedItems);
  var job=Assert.Single(page.Jobs);
  Assert.Equal(data.Url,job.Url);
  Assert.Equal(kind.StartsWith("dou",StringComparison.Ordinal)?"dou":"djinni",job.Source);
  Assert.Equal(job,Assert.Single(source.ParseListings(data.Xml)));
 }

 [Theory]
 [InlineData("dou-keyword")]
 [InlineData("djinni-keyword")]
 public async Task CrawlerPersistsRssLossEvidenceWithoutDroppingValidVacancy(string kind)
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var data=Fixture(kind);
  var source=CreateSource(kind);
  int feedCalls=0,detailCalls=0;
  using var http=new HttpClient(new Stub(req=>
  {
   if(IsFeedUrl(req.RequestUri!))
   {
    feedCalls++;
    return Response(data.Xml);
   }
   detailCalls++;
   return Response("<html><body><article>"+new string('D',180)+"</article></body></html>");
  }));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var report=await new Crawler(new HttpFetcher(http,0),storage,
   new RadarOptions{MaxPagesPerSource=3})
   .RunAsync([source],CancellationToken.None);
  var row=Assert.Single(report.Sources);
  Assert.Equal("PARTIAL",row.Status); // RSS cap remains unverified
  Assert.Equal(1,row.PagesFetched);
  Assert.Equal(4,row.RawRecords);
  Assert.Equal(3,row.DroppedRecords);
  Assert.Equal(1,row.ReferencesFound);
  Assert.Equal(1,row.DetailsFetched);
  Assert.Equal(0,row.DetailsFailed);
  Assert.Contains("RSS_REFERENCE_LOSS: unusable=2, duplicates=1",row.Error!);
  Assert.Equal(1,feedCalls);
  Assert.Equal(1,detailCalls);
  await using var db=new NpgsqlConnection(cs);
  await db.OpenAsync();
  await using var query=new NpgsqlCommand(
   "SELECT count(*) FROM job_queries WHERE source=@source AND url=@url AND query_name=@query",db);
  query.Parameters.AddWithValue("source",kind.StartsWith("dou",StringComparison.Ordinal)?"dou":"djinni");
  query.Parameters.AddWithValue("url",data.Url);
  query.Parameters.AddWithValue("query",source.Name);
  Assert.Equal(1L,await query.ExecuteScalarAsync());
 }

 [Theory]
 [InlineData("dou")]
 [InlineData("djinni")]
 public async Task EmptyRssIsNotVerifiedZeroVacancies(string kind)
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var source=CreateSource(kind);
  int httpCalls=0;
  using var http=new HttpClient(new Stub(_=>
  {
   httpCalls++;
   return Response("<rss><channel></channel></rss>");
  }));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var report=await new Crawler(new HttpFetcher(http,0),storage,new RadarOptions())
   .RunAsync([source],CancellationToken.None);
  var row=Assert.Single(report.Sources);
  Assert.Equal("PARTIAL",row.Status);
  Assert.Equal(1,row.PagesFetched);
  Assert.Equal(0,row.ReferencesFound);
  Assert.Equal(0,row.RawRecords);
  Assert.Equal(0,row.DroppedRecords);
  Assert.Null(row.ReportedTotal);
  Assert.Contains("RSS_EMPTY_UNVERIFIED",row.Error!);
  Assert.Equal(1,httpCalls);
 }

 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public async Task FetchAndXmlParseFailuresDoNotClaimRawCounts(bool badXml)
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var source=new DouKeywordFeed("failure-"+Guid.NewGuid().ToString("N"));
  using var http=new HttpClient(new Stub(_=>badXml
   ?Response("<rss><channel><item>")
   :new HttpResponseMessage(HttpStatusCode.Forbidden)));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var report=await new Crawler(new HttpFetcher(http,0),storage,new RadarOptions())
   .RunAsync([source],CancellationToken.None);
  var row=Assert.Single(report.Sources);
  Assert.Equal(badXml?"PARTIAL":"FAILED",row.Status);
  Assert.Equal(badXml?1:0,row.PagesFetched);
  Assert.Null(row.RawRecords);
  Assert.Null(row.DroppedRecords);
  Assert.Equal(0,row.ReferencesFound);
  Assert.NotNull(row.Error);
 }

 [Fact]
 public async Task DuplicateAcrossQueriesPreservesBothSearchProvenances()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var data=Fixture("dou");
  var xml=$"""
  <rss><channel>
  <item><title>Middle C# Engineer</title><link>{data.Url}</link></item>
  </channel></rss>
  """;
  int detailCalls=0;
  using var http=new HttpClient(new Stub(req=>
  {
   if(IsFeedUrl(req.RequestUri!))return Response(xml);
   detailCalls++;
   return Response("<html><body><article>"+new string('D',180)+"</article></body></html>");
  }));
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var report=await new Crawler(new HttpFetcher(http,0),storage,new RadarOptions())
   .RunAsync([new DouSource(),new DouKeywordFeed("C#")],CancellationToken.None);
  Assert.Equal(2,report.Sources.Count);
  Assert.All(report.Sources,row=>
  {
   Assert.Equal("PARTIAL",row.Status); // RSS is capped
   Assert.Equal(1,row.RawRecords);
   Assert.Equal(0,row.DroppedRecords);
   Assert.Equal(1,row.ReferencesFound);
   Assert.DoesNotContain("RSS_REFERENCE_LOSS",row.Error??"");
  });
  Assert.Equal(1,detailCalls);
  Assert.Equal(1,report.Sources.Sum(row=>row.DetailsFetched));
  Assert.Equal(1,report.Sources.Sum(row=>row.CachedDetails));
  await using var db=new NpgsqlConnection(cs);
  await db.OpenAsync();
  await using var cmd=new NpgsqlCommand(
   "SELECT count(*) FROM job_queries WHERE source='dou' AND url=@url",db);
  cmd.Parameters.AddWithValue("url",data.Url);
  Assert.Equal(2L,await cmd.ExecuteScalarAsync());
 }

 private static IJobSource CreateSource(string kind)=>kind switch
 {
  "dou"=>new DouSource(),
  "dou-keyword"=>new DouKeywordFeed("C#"),
  "djinni"=>new DjinniSource(),
  "djinni-keyword"=>new DjinniKeywordFeed("C#"),
  _=>throw new ArgumentOutOfRangeException(nameof(kind))
 };

 private static (string Url,string Xml) Fixture(string kind)
 {
  var id=Random.Shared.Next(1000000,9999999);
  var dou=kind.StartsWith("dou",StringComparison.Ordinal);
  var good=dou?$"https://jobs.dou.ua/vacancies/{id}/":
   $"https://djinni.co/jobs/{id}-middle-backend/";
  var repeated=good+"?utm_source=feed";
  var bad=dou?"https://other.example/vacancies/2/":
   "https://other.example/jobs/2/";
  var missing=dou?$"https://jobs.dou.ua/vacancies/{id+1}/":
   $"https://djinni.co/jobs/{id+1}-empty/";
  var xml=$"""
  <rss><channel>
  <item><title>Middle .NET Backend Developer</title><link>{good}</link></item>
  <item><title>Middle .NET Backend Developer</title><link>{repeated}</link></item>
  <item><title>Invalid employer link</title><link>{bad}</link></item>
  <item><title> </title><link>{missing}</link></item>
  </channel></rss>
  """;
  return (good,xml);
 }

 private static bool IsFeedUrl(Uri uri)=>
  uri.AbsolutePath.Contains("/feeds/",StringComparison.Ordinal)||
  uri.AbsolutePath.Contains("/jobs/rss/",StringComparison.Ordinal);

 private static HttpResponseMessage Response(string body)=>new(HttpStatusCode.OK)
 {Content=new StringContent(body)};

 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
   =>Task.FromResult(respond(req));
 }
}
