using System.Net;
using System.Text;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class RemoteBoardsTests
{
 private const string Rss="""
 <?xml version="1.0" encoding="utf-8"?>
 <rss version="2.0"><channel><title>We Work Remotely</title>
  <item>
   <title>Middle .NET Backend Engineer</title>
   <link>https://weworkremotely.com/remote-jobs/example-middle-net</link>
   <pubDate>Sat, 10 Oct 2026 07:00:00 GMT</pubDate>
   <description><![CDATA[<p>Fully remote ASP.NET Core, Web API, PostgreSQL, EF Core.</p>]]></description>
  </item>
  <item>
   <title>Untrusted</title>
   <link>https://another.example/remote-jobs/evil</link>
  </item>
 </channel></rss>
 """;

 // Clean fixture for successful multi-source scans. The separate Rss fixture
 // intentionally contains an unsafe URL to test dropped-record accounting.
 private const string CleanRss="""
 <?xml version="1.0" encoding="utf-8"?>
 <rss version="2.0"><channel><title>We Work Remotely</title>
  <item>
   <title>Middle .NET Backend Engineer</title>
   <link>https://weworkremotely.com/remote-jobs/example-middle-net</link>
   <pubDate>Sat, 10 Oct 2026 07:00:00 GMT</pubDate>
   <description><![CDATA[<p>Fully remote ASP.NET Core, Web API, PostgreSQL, EF Core.</p>]]></description>
  </item>
 </channel></rss>
 """;

 private const string Remotive="""
 {
  "job-count":2,
  "jobs":[
   {"id":101,"url":"https://remotive.com/remote-jobs/software-dev/dotnet-backend-101",
    "title":"Middle .NET Backend Engineer","company_name":"Remote Client",
    "candidate_required_location":"Worldwide","job_type":"contract",
    "publication_date":"2026-10-09T07:00:00",
    "salary":"$3,000-$4,000/month",
    "description":"<p>Fully remote ASP.NET Core REST API PostgreSQL EF Core RabbitMQ.</p>"},
   {"id":102,"url":"https://remotive.com/remote-jobs/software-dev/dotnet-only-us-102",
    "title":"Middle .NET Backend Engineer","company_name":"US Only",
    "candidate_required_location":"USA only",
    "publication_date":"2026-10-09T08:00:00",
    "description":"<p>Fully remote ASP.NET Core REST API PostgreSQL EF Core RabbitMQ.</p>"}
  ]
 }
 """;

 [Fact]
 public void WwrRssIsAttributedAndIncompleteTextNeverBecomesLikelyFit()
 {
  var page=RemoteBoards.ParseWwr(Rss,"wwr-backend");
  Assert.Equal(2,page.RawCount);
  Assert.Equal(1,page.DroppedCount);
  var job=Assert.Single(page.Items);
  Assert.Equal("We Work Remotely",job.Attribution);
  Assert.False(job.HasFullText);
  Assert.Contains("ASP.NET Core",job.Excerpt);
  Assert.Equal(FitBucket.NeedsReview,RemoteBoards.Assess(job).Bucket);
 }

 [Fact]
 public void RemotiveWorldwideCanBeLikelyButCountryRestrictedNeedsReview()
 {
  var page=RemoteBoards.ParseRemotive(Remotive);
  Assert.Equal(2,page.RawCount);
  Assert.Equal(0,page.DroppedCount);
  Assert.Equal("contract",page.Items[0].JobType);
  Assert.Equal("Remotive",page.Items[0].Attribution);
  Assert.Equal(FitBucket.LikelyFit,RemoteBoards.Assess(page.Items[0]).Bucket);
  var restricted=RemoteBoards.Assess(page.Items[1]);
  Assert.Equal(FitBucket.NeedsReview,restricted.Bucket);
  Assert.Contains(restricted.Warnings,w=>w.Contains("Ukraine"));
 }

 [Theory]
 [InlineData("Worldwide except Ukraine")]
 [InlineData("Europe, Ukraine")]
 [InlineData("Ukraine not eligible")]
 [InlineData("EMEA")]
 public void AmbiguousOrNegativeUkraineLocationsAreNeverConfidentFit(string location)
 {
  var opening=RemoteBoards.ParseRemotive(Remotive).Items[0] with
   {CandidateLocation=location};
  var a=RemoteBoards.Assess(opening);
  Assert.Equal(FitBucket.NeedsReview,a.Bucket);
  Assert.Contains(a.Warnings,w=>w.Contains("Ukraine"));
 }

 [Fact]
 public void ARequirementAfter2400CharactersStillBlocksLikelyFit()
 {
  var longDescription=
   "Fully remote ASP.NET Core REST API PostgreSQL EF Core RabbitMQ. "+
   new string('x',2500)+
   " English B2 required for daily meetings.";
  var json=JsonSerializer.Serialize(new
  {
   jobs=new[]{new{
    id=900L,
    url="https://remotive.com/remote-jobs/software-dev/long-text-900",
    title="Middle .NET Backend Engineer",company_name="Example",
    candidate_required_location="Worldwide",description=longDescription
   }}
  });
  var opening=Assert.Single(RemoteBoards.ParseRemotive(json).Items);
  Assert.True(opening.HasFullText);
  Assert.True(opening.Excerpt.Length<=2401);
  Assert.DoesNotContain("English B2",opening.Excerpt);
  var assessed=RemoteBoards.Assess(opening);
  Assert.Equal(FitBucket.NeedsReview,assessed.Bucket);
  Assert.Contains(assessed.Warnings,w=>w.Contains("B2+"));
  using var emitted=JsonDocument.Parse(JsonlOutput.Serialize(assessed));
  Assert.False(emitted.RootElement.GetProperty("Opening")
   .TryGetProperty("FullDescription",out _));
 }

 [Theory]
 [InlineData("{}")]
 [InlineData("{\"jobs\":null}")]
 [InlineData("{\"jobs\":{}}")]
 public void MalformedRemotiveResponseDoesNotBecomeEmptyResult(string json)
 {
  Assert.Throws<InvalidDataException>(()=>RemoteBoards.ParseRemotive(json));
 }

 [Fact]
 public void MaliciousRemotiveUrlsAreDroppedAndReported()
 {
  const string json="""
  {"jobs":[
   {"id":3,"url":"https://remotive.com.evil.example/remote-jobs/foo","title":"Wrong host"},
   {"id":4,"url":"http://remotive.com/remote-jobs/foo","title":"Insecure"},
   {"id":5,"url":"https://remotive.com/remote-jobs/valid","title":"Valid .NET"}
  ]}
  """;
  var result=RemoteBoards.ParseRemotive(json);
  Assert.Equal(3,result.RawCount);
  Assert.Equal(2,result.DroppedCount);
  Assert.Single(result.Items);
 }

 [Fact]
 public async Task MixedProvidersMergeWwrDuplicatesAndKeepSourceAttribution()
 {
  int count=0;
  using var http=new HttpClient(new Handler(req=>{
   count++;
   if(req.RequestUri!.Host=="remotive.com")
    return Ok(Remotive,"application/json");
   return Ok(CleanRss,"application/rss+xml");
  }));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanAsync(http,output,null,CancellationToken.None);
  Assert.Equal(3,count);
  Assert.Equal(3,report.Sources.Count);
  Assert.Equal(3,report.UniqueJobs); // 1 WWR from two feeds + 2 Remotive
  Assert.Equal("PARTIAL",report.CoverageStatus); // no exhaustive claim
  var lines=output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries);
  Assert.Equal(3,lines.Length);
  var attributions=lines.Select(line=>{
   using var document=JsonDocument.Parse(line);
   return document.RootElement.GetProperty("Opening").GetProperty("Attribution").GetString();
  }).ToArray();
  Assert.Contains("We Work Remotely",attributions);
  Assert.Contains("Remotive",attributions);
  using var row=JsonDocument.Parse(lines[0]);
  Assert.True(row.RootElement.TryGetProperty("Warnings",out _));
 }

 [Fact]
 public async Task ProviderFailureIsReportedWithoutLosingOtherResults()
 {
  using var http=new HttpClient(new Handler(req=>
    req.RequestUri!.Host=="remotive.com"
     ? new HttpResponseMessage((HttpStatusCode)429)
     : Ok(CleanRss,"application/rss+xml")));
  using var output=new StringWriter();
  var report=await RemoteBoards.ScanAsync(http,output,null,CancellationToken.None);
  Assert.Equal("FAILED_OR_DROPPED",report.CoverageStatus);
  Assert.Contains(report.Sources,s=>s.Source=="remotive"&&s.Status=="FAILED");
  var lines=output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries);
  var job=Assert.Single(lines);
  using var doc=JsonDocument.Parse(job);
  Assert.Equal("We Work Remotely",
   doc.RootElement.GetProperty("Opening").GetProperty("Attribution").GetString());
 }

 private static HttpResponseMessage Ok(string body,string contentType)=>
  new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,contentType)};

 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> answer):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>
   Task.FromResult(answer(request));
 }
}
