using System.Net;
using System.Text;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class FreelancehuntProjectsTests
{
 private const string Example="""
 {
  "data":[
   {"id":"299165","type":"project",
    "attributes":{"name":"Потрібна інтеграція API C# .NET",
      "description":"Налаштувати REST API та брокерське виконання угод.",
      "skills":[{"id":1,"name":"C#"},{"id":2,"name":"API"}],
      "status":{"id":11,"name":"Open for proposals"},
      "budget":{"amount":2300,"currency":"UAH"},
      "bid_count":4,"is_only_for_plus":false,"is_remote_job":false,
      "published_at":"2026-10-10T10:00:00+03:00"},
    "links":{"self":{"api":"https://api.freelancehunt.com/v2/projects/299165",
      "web":"https://freelancehunt.com/project/dotnet-api/299165.html"}}},
   {"id":299166,"type":"project",
    "attributes":{"name":"Design a logo","description_html":"<p>Graphic design only</p>",
      "budget":{"amount":200,"currency":"UAH"},"is_only_for_plus":true},
    "links":{"self":{"web":"https://freelancehunt.com/project/logo/299166.html"}}}
  ],
  "links":{"self":"https://api.freelancehunt.com/v2/projects?page[number]=1","next":null}
 }
 """;

 [Fact]
 public void ParseProjectsAndPreserveProviderMetadata()
 {
  var page=FreelancehuntProjects.ParsePage(Example);
  Assert.Equal(2,page.RawCount);
  Assert.Equal(0,page.DroppedCount);
  Assert.False(page.HasNext);
  Assert.True(page.NextKnown);
  var p=page.Projects[0];
  Assert.Equal(299165,p.Id);
  Assert.Equal("https://freelancehunt.com/project/dotnet-api/299165.html",p.Url);
  Assert.Equal(2300m,p.Budget);
  Assert.Equal("UAH",p.Currency);
  Assert.Equal(4,p.BidCount);
  Assert.Equal(false,p.RemoteOnly);
  Assert.Contains("C#",p.Skills);
  Assert.NotNull(p.PublishedAt);
  Assert.Equal("Priority",FreelancehuntProjects.Rank(p).Bucket);
  Assert.Equal("LowMatch",FreelancehuntProjects.Rank(page.Projects[1]).Bucket);
 }

 [Fact]
 public void PlusOnlyProjectGetsReviewEvenWhenRelevant()
 {
  var project=FreelancehuntProjects.ParsePage(Example).Projects[0] with {PlusOnly=true};
  var fit=FreelancehuntProjects.Rank(project);
  Assert.Equal("Review",fit.Bucket);
  Assert.Contains(fit.Reasons,r=>r.Contains("Plus"));
 }

 [Theory]
 [InlineData("{}")]
 [InlineData("{\"data\":null}")]
 [InlineData("{\"data\":{}}")]
 public void InvalidResponseIsNotSilentlyTreatedAsNoJobs(string json)
 {
  Assert.Throws<InvalidDataException>(()=>FreelancehuntProjects.ParsePage(json));
 }

 [Fact]
 public void UntrustedUrlsAndMissingIdsRemainCountedAsDropped()
 {
  const string response="""
   {"data":[
    {"id":42,"attributes":{"name":"API development"},"links":{"self":{"web":"https://evil.example/project/x/42.html"}}},
    {"id":43,"attributes":{"name":"API development"},"links":{"self":{"web":"http://freelancehunt.com/project/x/43.html"}}},
    {"id":44,"attributes":{"name":"API development"},"links":{"self":{"web":"https://freelancehunt.com/project/ok/44.html"}}},
    {"id":-2,"attributes":{"name":"Invalid ID"},"links":{"self":{"web":"https://freelancehunt.com/project/id/2.html"}}}
   ],"links":{"next":null}}
  """;
  var result=FreelancehuntProjects.ParsePage(response);
  Assert.Equal(4,result.RawCount);
  Assert.Equal(3,result.DroppedCount);
  Assert.Single(result.Projects);
  Assert.Equal(44,result.Projects[0].Id);
 }

 [Theory]
 [InlineData(0)]
 [InlineData(101)]
 public void InvalidPageNumberIsRejected(int page)
 {
  Assert.Throws<ArgumentOutOfRangeException>(()=>FreelancehuntProjects.PageUri(page,[]));
 }

 [Fact]
 public void SkillFilterUsesProviderDocumentedQueryAndNumericIdsOnly()
 {
  var uri=FreelancehuntProjects.PageUri(2,[99,69]);
  Assert.StartsWith("https://api.freelancehunt.com/v2/projects?",uri.AbsoluteUri);
  Assert.Contains("page%5Bnumber%5D=2",uri.AbsoluteUri);
  Assert.Contains("filter%5Bskill_id%5D=69%2C99",uri.AbsoluteUri);
  Assert.Throws<ArgumentException>(()=>FreelancehuntProjects.PageUri(1,[-1]));
 }

 [Fact]
 public async Task NoTokenMeansNoHttpAndNoOutput()
 {
  int calls=0;
  using var client=new HttpClient(new Stub(_=>{
   calls++;
   return Ok(Example);
  }));
  using var text=new StringWriter();
  await Assert.ThrowsAsync<InvalidOperationException>(()=>
   FreelancehuntProjects.ScanAsync(client,text,null,[],2,null,CancellationToken.None));
  Assert.Equal(0,calls);
  Assert.Empty(text.ToString());
 }

 [Fact]
 public async Task SearchUsesBearerOnlyAndDoesNotRequireDatabase()
 {
  int calls=0;
  using var client=new HttpClient(new Stub(req=>{
   Assert.Equal("Bearer",req.Headers.Authorization?.Scheme);
   Assert.Equal("never-serialize-this",req.Headers.Authorization?.Parameter);
   Assert.Equal(HttpMethod.Get,req.Method);
   Assert.Equal("api.freelancehunt.com",req.RequestUri!.Host);
   calls++;
   return Ok(Example);
  }));
  using var text=new StringWriter();
  var report=await FreelancehuntProjects.ScanAsync(client,text,"never-serialize-this",
   [],2,null,CancellationToken.None);
  Assert.Equal(1,calls);
  Assert.Equal(1,report.PagesFetched);
  Assert.Equal(2,report.UniqueProjects);
  Assert.Equal(1,report.Priority);
  Assert.Equal("PARTIAL",report.CoverageStatus);
  Assert.False(report.IncompleteEnumeration);
  Assert.DoesNotContain("never-serialize-this",text.ToString());
  var lines=text.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries);
  Assert.Equal(2,lines.Length);
  using var row=JsonDocument.Parse(lines[0]);
  Assert.Equal(299165,row.RootElement.GetProperty("Project").GetProperty("Id").GetInt64());
 }

 [Fact]
 public async Task PaginatedDuplicateIdsAreDeduplicated()
 {
  int calls=0;
  using var client=new HttpClient(new Stub(req=>{
   calls++;
   string json=Example.Replace("\"next\":null",
     calls==1?"\"next\":\"https://api.freelancehunt.com/v2/projects?page[number]=2\"":"\"next\":null");
   return Ok(json);
  }));
  using var output=new StringWriter();
  var report=await FreelancehuntProjects.ScanAsync(client,output,"token",[],2,null,CancellationToken.None);
  Assert.Equal(2,calls);
  Assert.Equal(4,report.RawRecords);
  Assert.Equal(2,report.UniqueProjects);
  Assert.False(report.IncompleteEnumeration);
  Assert.Equal(2,output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries).Length);
 }

 [Fact]
 public async Task PageCapMakesIncompleteEnumerationExplicit()
 {
  using var client=new HttpClient(new Stub(_=>Ok(
    Example.Replace("\"next\":null",
    "\"next\":\"https://api.freelancehunt.com/v2/projects?page[number]=2\""))));
  using var output=new StringWriter();
  var report=await FreelancehuntProjects.ScanAsync(client,output,"token",[],1,null,CancellationToken.None);
  Assert.True(report.IncompleteEnumeration);
  Assert.Equal("PARTIAL",report.CoverageStatus);
 }

 [Theory]
 [InlineData(HttpStatusCode.Forbidden)]
 [InlineData(HttpStatusCode.Unauthorized)]
 [InlineData((HttpStatusCode)429)]
 public async Task DeniedAndRateLimitedRequestsNeverRetry(HttpStatusCode status)
 {
  int calls=0;
  using var client=new HttpClient(new Stub(_=>{
   calls++;
   return new HttpResponseMessage(status);
  }));
  using var output=new StringWriter();
  var ex=await Assert.ThrowsAsync<InvalidDataException>(()=>
   FreelancehuntProjects.ScanAsync(client,output,"private-token",[],3,null,CancellationToken.None));
  Assert.Equal(1,calls);
  Assert.Empty(output.ToString());
  Assert.DoesNotContain("private-token",ex.Message);
 }

 private static HttpResponseMessage Ok(string body)=>
  new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};

 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> answer):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)=>
   Task.FromResult(answer(req));
 }
}
