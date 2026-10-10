using System.Net;
using System.Text;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class WwrIntegrationTests
{
 private static string Item(string url,string title="Middle C# .NET Backend Engineer",
  string extra="",string? date="Sat, 10 Oct 2026 07:00:00 GMT",
  string company="")=>
  "<item><title>"+System.Security.SecurityElement.Escape(title)+"</title>"+
  "<link>"+System.Security.SecurityElement.Escape(url)+"</link>"+
  (date is null?"":"<pubDate>"+System.Security.SecurityElement.Escape(date)+"</pubDate>")+
  (company.Length==0?"":"<company>"+System.Security.SecurityElement.Escape(company)+"</company>")+
  "<description><![CDATA[<p>Fully remote ASP.NET Core Web API PostgreSQL. "+
  extra+"</p>]]></description></item>";

 private static string Rss(params string[] items)=>
  "<?xml version=\"1.0\" encoding=\"utf-8\"?><rss version=\"2.0\"><channel>"+
  string.Concat(items)+"</channel></rss>";

 private static HttpResponseMessage Ok(string data)=>
  new(HttpStatusCode.OK){Content=new StringContent(data,Encoding.UTF8,"application/rss+xml")};

 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> action):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
   CancellationToken cancellationToken)=>Task.FromResult(action(request));
 }

 private const string A="https://weworkremotely.com/remote-jobs/acme-middle-net-123";
 private const string B="https://weworkremotely.com/remote-jobs/other-middle-net-456";

 [Fact]
 public async Task TwoWwrFeedsDeduplicateCanonicalUrlAndKeepBothQueries()
 {
  var requested=new List<string>();
  using var client=new HttpClient(new Handler(req=>
  {
   requested.Add(req.RequestUri!.AbsoluteUri);
   return req.RequestUri!.AbsoluteUri==RemoteBoards.WwrBackend
    ? Ok(Rss(Item(A+"?utm_medium=rss",company:"Acme")))
    : Ok(Rss(Item("https://www.weworkremotely.com/remote-jobs/acme-middle-net-123/#top")));
  }));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanWwrAsync(client,output,null,CancellationToken.None);
  Assert.Equal(2,requested.Count);
  Assert.DoesNotContain(requested,u=>u.Contains("remotive.com",StringComparison.OrdinalIgnoreCase));
  Assert.Equal(1,report.UniqueJobs);
  Assert.Equal(1,report.DuplicateRecords);
  Assert.Equal(2,report.Sources.Count);
  Assert.All(report.Sources,s=>Assert.Equal(1,s.ParsedRecords));
  Assert.Equal(0,report.Sources[0].DuplicateRecords);
  Assert.Equal(1,report.Sources[1].DuplicateRecords);
  Assert.Equal(1,report.Sources[0].AcceptedRecords);
  Assert.Equal(0,report.Sources[1].AcceptedRecords);
  Assert.Equal("PARTIAL",report.CoverageStatus);
  var line=Assert.Single(output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries));
  var candidate=JsonSerializer.Deserialize<RemoteBoardCandidate>(line,JsonlOutput.Options)!;
  Assert.Equal(A,candidate.Opening.Url);
  Assert.Equal("wwr-backend",candidate.Opening.Source);
  Assert.Equal("wwr",candidate.Opening.CanonicalSource);
  Assert.Equal(new[]{"wwr-backend","wwr-programming"},candidate.Opening.FeedQueries);
  Assert.Equal("Acme",candidate.Opening.Company);
  Assert.Equal("We Work Remotely",candidate.Opening.Attribution);
  Assert.NotNull(candidate.Opening.ObservedAt);
  Assert.False(candidate.Opening.HasFullText);
  Assert.NotEqual(FitBucket.LikelyFit,candidate.Bucket);
 }

 [Fact]
 public async Task FullstackFeedOptInAddsOnlyNewCanonicalPostings()
 {
  int requests=0;
  using var client=new HttpClient(new Handler(req=>
  {
   requests++;
   return req.RequestUri!.AbsoluteUri switch
   {
    RemoteBoards.WwrBackend=>Ok(Rss(Item(A))),
    RemoteBoards.WwrProgramming=>Ok(Rss(Item(A+"#rss"))),
    RemoteBoards.WwrFullstack=>Ok(Rss(Item(A),Item(B))),
    _=>throw new InvalidOperationException("Unexpected URL")
   };
  }));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanWwrAsync(client,output,null,CancellationToken.None,
   includeFullstack:true);
  Assert.Equal(3,requests);
  Assert.Equal(2,report.UniqueJobs);
  Assert.Equal(2,report.DuplicateRecords);
  Assert.Equal(2,report.Sources[2].ParsedRecords);
  Assert.Equal(1,report.Sources[2].DuplicateRecords);
  Assert.Equal("wwr-fullstack",report.Sources[2].Source);
  var candidates=output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries)
   .Select(x=>JsonSerializer.Deserialize<RemoteBoardCandidate>(x,JsonlOutput.Options)!).ToArray();
  Assert.Equal(2,candidates.Length);
  Assert.Equal(3,Assert.Single(candidates,x=>x.Opening.Url==A).Opening.FeedQueries.Count);
  Assert.Equal(new[]{"wwr-fullstack"},
   Assert.Single(candidates,x=>x.Opening.Url==B).Opening.FeedQueries);
  Assert.Equal("PARTIAL",report.CoverageStatus);
 }

 [Fact]
 public async Task ExistingMixedCommandStillCallsRemotiveExactlyOnce()
 {
  int wwr=0,remotive=0;
  const string remotivePayload="""{"jobs":[]}""";
  using var client=new HttpClient(new Handler(req=>
  {
   if(req.RequestUri!.Host=="remotive.com")
   {
    remotive++;
    return new HttpResponseMessage(HttpStatusCode.OK)
    {Content=new StringContent(remotivePayload,Encoding.UTF8,"application/json")};
   }
   wwr++;
   return Ok(Rss(Item(A)));
  }));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanAsync(client,output,null,CancellationToken.None);
  Assert.Equal(2,wwr);
  Assert.Equal(1,remotive);
  Assert.Equal(3,report.Sources.Count);
  Assert.Equal(1,report.UniqueJobs);
  Assert.Equal("remotive",report.Sources[2].Source);
  Assert.Equal("PARTIAL",report.CoverageStatus);
 }

 [Theory]
 [InlineData("http://weworkremotely.com/remote-jobs/abc")]
 [InlineData("https://weworkremotely.com.evil.example/remote-jobs/abc")]
 [InlineData("https://evil.example/remote-jobs/abc")]
 [InlineData("https://user:pass@weworkremotely.com/remote-jobs/abc")]
 [InlineData("https://weworkremotely.com:444/remote-jobs/abc")]
 [InlineData("https://weworkremotely.com/categories/remote-programming-jobs.rss")]
 [InlineData("https://weworkremotely.com/remote-jobs/")]
 [InlineData("https://weworkremotely.com/remote-jobs/%2Fadmin")]
 public void BadWwrUrlsAreDroppedWithExplicitCounts(string url)
 {
  var page=RemoteBoards.ParseWwr(Rss(Item(url)),"wwr-backend");
  Assert.Equal(1,page.RawCount);
  Assert.Equal(1,page.DroppedCount);
  Assert.Empty(page.Items);
 }

 [Fact]
 public void BadOrMissingRssValuesDoNotInventFieldsOrActiveStatus()
 {
  var data=Rss(
   Item(A,extra:"<strong>API &amp; integration</strong>",date:"not-a-date"),
   "<item><title>No URL</title><description>irrelevant</description></item>",
   "<item><link>"+B+"</link></item>");
  var page=RemoteBoards.ParseWwr(data,"wwr-backend");
  Assert.Equal(3,page.RawCount);
  Assert.Equal(2,page.DroppedCount);
  Assert.Equal(1,page.InvalidDates);
  var item=Assert.Single(page.Items);
  Assert.Null(item.PublishedAt);
  Assert.Null(item.Company);
  Assert.Null(item.CandidateLocation);
  Assert.False(item.HasFullText);
  Assert.Equal("wwr",RemoteBoards.CanonicalSource(item.Source));
  Assert.Equal("wwr-backend",item.Source);
  Assert.Equal("API & integration",
   item.FullDescription!.Contains("API & integration")?"API & integration":"missing");
  var assessment=RemoteBoards.Assess(item);
  Assert.Equal(FitBucket.NeedsReview,assessment.Bucket);
 }

 [Theory]
 [InlineData("Europe Only")]
 [InlineData("Anywhere in the World but Ukraine excluded")]
 [InlineData("US Only")]
 [InlineData("EU residents only")]
 [InlineData("Ukraine allowed")]
 [InlineData("Remote from Ukraine")]
 [InlineData("EMEA / CET or EET")]
 [InlineData("Must reside in Germany")]
 [InlineData("English B2 spoken required")]
 public void GeographyAndLanguageNeverUpgradeRssToLikelyFit(string sentence)
 {
  var item=Assert.Single(RemoteBoards.ParseWwr(
   Rss(Item(A,extra:sentence)),"wwr-programming").Items);
  var assessed=RemoteBoards.Assess(item);
  Assert.NotEqual(FitBucket.LikelyFit,assessed.Bucket);
  Assert.False(item.HasFullText);
  Assert.NotNull(item.ObservedAt);
 }

 [Fact]
 public void CanonicalJobRefProjectionIsInMemoryOnlyAndPreservesQueries()
 {
  var opening=Assert.Single(RemoteBoards.ParseWwr(Rss(
   Item(A,company:"Remote Acme")),"wwr-backend").Items);
  var projected=WwrEmploymentProjection.Project(opening);
  Assert.Equal("wwr",projected.Reference.Source);
  Assert.Equal(A,projected.Reference.Url);
  Assert.Equal(opening.Title,projected.Reference.Title);
  Assert.Equal("Remote Acme",projected.Reference.Company);
  Assert.Equal(opening.Excerpt,projected.Reference.Preview);
  Assert.Equal(new[]{"wwr-backend"},projected.QueryNames);
  Assert.Equal("We Work Remotely",projected.Attribution);
  Assert.NotNull(projected.ObservedAt);
  Assert.False(projected.HasFullText);
  Assert.Null(projected.OpenStatus);
  Assert.Throws<ArgumentException>(()=>WwrEmploymentProjection.Project(
   opening with {Source="remotive"}));
 }

 [Fact]
 public void DefaultOptionsAndScheduledFeedVariantsExcludeWwr()
 {
  var options=new RadarOptions();
  Assert.False(options.EnabledWwr);
  Assert.DoesNotContain(FeedVariants.Create(options),
   source=>source.Name.StartsWith("wwr-",StringComparison.OrdinalIgnoreCase));
  // Even if explicitly configured, the regular scheduler does not ingest WWR
  // pending a separate provider-retention authorization and go-live decision.
  options.EnabledWwr=true;
  Assert.DoesNotContain(FeedVariants.Create(options),
   source=>source.Name.StartsWith("wwr-",StringComparison.OrdinalIgnoreCase));
 }

 [Fact]
 public void HtmlDocumentMasqueradingAsRssIsRejected()
 {
  Assert.Throws<InvalidDataException>(()=>RemoteBoards.ParseWwr(
   "<html><body><item><title>Fake vacancy</title><link>"+
    A+"</link></item></body></html>","wwr-backend"));
 }

 [Fact]
 public void XmlDtdIsNotAccepted()
 {
  const string xml="<!DOCTYPE foo [ <!ENTITY xxe SYSTEM 'file:///etc/passwd'> ]>"+
   "<rss><channel><item><title>&xxe;</title></item></channel></rss>";
  Assert.Throws<System.Xml.XmlException>(()=>RemoteBoards.ParseWwr(xml,"wwr-backend"));
 }

 [Fact]
 public void InvalidFeedNameCannotFakeProviderProvenance()
 {
  Assert.Throws<ArgumentException>(()=>RemoteBoards.ParseWwr(Rss(Item(A)),"wwr-paid"));
 }

 [Theory]
 [InlineData(429)]
 [InlineData(403)]
 [InlineData(500)]
 public async Task PartialOutageDoesNotEraseOtherFeedOrTriggerRetry(int status)
 {
  int badCalls=0,goodCalls=0;
  using var client=new HttpClient(new Handler(req=>
  {
   if(req.RequestUri!.AbsoluteUri==RemoteBoards.WwrBackend)
   {
    badCalls++;
    return new HttpResponseMessage((HttpStatusCode)status);
   }
   goodCalls++;
   return Ok(Rss(Item(B)));
  }));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanWwrAsync(client,output,null,CancellationToken.None);
  Assert.Equal(1,badCalls);
  Assert.Equal(1,goodCalls);
  Assert.Equal(1,report.UniqueJobs);
  Assert.Equal("FAILED",report.Sources[0].Status);
  Assert.Equal("PARTIAL",report.Sources[1].Status);
  Assert.Equal("FAILED_OR_DROPPED",report.CoverageStatus);
  Assert.NotEmpty(output.ToString());
 }

 [Fact]
 public async Task UnsafeRedirectIsNotFollowed()
 {
  var requested=new List<string>();
  using var client=new HttpClient(new Handler(req=>
  {
   requested.Add(req.RequestUri!.Host);
   if(req.RequestUri!.AbsoluteUri==RemoteBoards.WwrBackend)
    return new HttpResponseMessage(HttpStatusCode.Found)
    {Headers={Location=new Uri("https://evil.example/redirect")}};
   return Ok(Rss(Item(B)));
  }));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanWwrAsync(client,output,null,CancellationToken.None);
  Assert.Equal(2,requested.Count);
  Assert.DoesNotContain("evil.example",requested);
  Assert.Equal("FAILED",report.Sources[0].Status);
 }

 [Fact]
 public async Task OversizedFeedFailsClosedWithoutDroppingOtherFeed()
 {
  var reqs=0;
  using var client=new HttpClient(new Handler(req=>
  {
   reqs++;
   if(req.RequestUri!.AbsoluteUri==RemoteBoards.WwrBackend)
    return Ok(new string('x',2_000_001));
   return Ok(Rss(Item(B)));
  }));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanWwrAsync(client,output,null,CancellationToken.None);
  Assert.Equal(2,reqs);
  Assert.Equal(1,report.UniqueJobs);
  Assert.Equal("FAILED",report.Sources[0].Status);
  Assert.Contains("limit",report.Sources[0].Error!,StringComparison.OrdinalIgnoreCase);
 }

 [Fact]
 public async Task MalformedRssSourceDoesNotHideAnotherFeed()
 {
  using var client=new HttpClient(new Handler(req=>
   req.RequestUri!.AbsoluteUri==RemoteBoards.WwrBackend
    ?Ok("<rss><channel><item>"):Ok(Rss(Item(B)))));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanWwrAsync(client,output,null,CancellationToken.None);
  Assert.Equal(1,report.UniqueJobs);
  Assert.Equal("FAILED",report.Sources[0].Status);
  Assert.NotNull(report.Sources[0].Error);
 }

 [Fact]
 public async Task EmptyFeedRemainsPartialAndDoesNotClaimNoVacancies()
 {
  using var client=new HttpClient(new Handler(_=>Ok(Rss())));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanWwrAsync(client,output,null,CancellationToken.None);
  Assert.Equal(0,report.UniqueJobs);
  Assert.Equal("PARTIAL",report.CoverageStatus);
  Assert.All(report.Sources,s=>Assert.Contains("does not establish zero",s.CoverageWarning!));
 }

 [Fact]
 public async Task WwrDirectFileModeUsesExistingUtf8IntegrityAndNoDatabase()
 {
  using var client=new HttpClient(new Handler(_=>Ok(Rss(
    Item(A,"Middle .NET Backend Engineer",extra:"Україна + worldwide — “quotes” •")))));
  var root=Path.Combine(Path.GetTempPath(),"jobradar-wwr-"+Guid.NewGuid().ToString("N"));
  var path=Path.Combine(root,"wwr-only.jsonl");
  try
  {
   var result=await RemoteBoardFileExport.WriteAsync(client,path,null,CancellationToken.None,
    wwrOnly:true);
   Assert.Equal(1,result.Report.UniqueJobs);
   Assert.True(result.Integrity.Valid);
   Assert.Equal(1,result.Integrity.Lines);
   var content=await File.ReadAllTextAsync(path,new UTF8Encoding(false,true));
   Assert.Contains("Україна",content);
   Assert.Contains("We Work Remotely",content);
   Assert.DoesNotContain("remotive.com",content);
  }
  finally {if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
