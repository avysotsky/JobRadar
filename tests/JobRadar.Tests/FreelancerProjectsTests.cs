using System.Net;
using System.Text;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class FreelancerProjectsTests
{
 private const string JsonFixture="""
 {
  "status":"success",
  "result":{"projects":[
   {"id":12345,"seo_url":"build-dotnet-exchange-api-12345",
    "title":"Build C# .NET trading execution integration",
    "preview_description":"Connect a broker REST API to order execution with webhooks.",
    "currency":{"code":"USD"},"budget":{"minimum":100,"maximum":500},
    "type":"FIXED","jobs":[{"id":1,"name":"C# Programming"},{"name":".NET"}]},
   {"id":45678,"seo_url":"marketing-campaign-45678",
    "title":"Design a promotional logo","preview_description":"Graphic design for a logo.",
    "budget":{"minimum":50,"maximum":120},"jobs":[{"name":"Graphic Design"}]}
  ]}
 }
 """;

 [Fact]
 public void ParsesProjectsWithBudgetsSkillsAndSafeCanonicalUrls()
 {
  var jobs=FreelancerProjects.ParseSearch(JsonFixture);
  Assert.Equal(2,jobs.Count);
  var first=jobs[0];
  Assert.Equal(12345,first.Id);
  Assert.Equal("https://www.freelancer.com/projects/build-dotnet-exchange-api-12345",first.Url);
  Assert.Equal("USD",first.Currency);
  Assert.Equal(100m,first.BudgetMinimum);
  Assert.Equal(500m,first.BudgetMaximum);
  Assert.Contains("C# Programming",first.Skills);
  Assert.Equal("Priority",FreelancerProjects.Rank(first,[".NET"]).Bucket);
  Assert.Equal("LowMatch",FreelancerProjects.Rank(jobs[1],["logo"]).Bucket);
 }

 [Fact]
 public void InvalidUrlsAndRecordsAreNotTrusted()
 {
  const string json="""
  {"result":{"projects":[
    {"id":"901","seo_url":"../../evil","title":"Backend API","preview_description":"Hello"},
    {"id":-1,"title":"Invalid"},
    {"id":902,"title":""}
  ]}}
  """;
  var jobs=FreelancerProjects.ParseSearch(json);
  Assert.Single(jobs);
  Assert.Equal("https://www.freelancer.com/projects/901",jobs[0].Url);
 }

 [Theory]
 [InlineData("{}")]
 [InlineData("{\"status\":\"error\",\"result\":{\"projects\":[]}}")]
 [InlineData("{\"result\":{\"projects\":{}}}")]
 public void IncorrectApiShapeCannotSilentlyPass(string response)
 {
  Assert.Throws<InvalidDataException>(()=>FreelancerProjects.ParseSearch(response));
 }

 [Fact]
 public void SearchUriEscapesQueriesAndLimits()
 {
  var uri=FreelancerProjects.SearchUri("C# .NET / REST",20,40);
  Assert.Equal("www.freelancer.com",uri.Host);
  Assert.Contains("C%23",uri.AbsoluteUri);
  Assert.Contains("limit=20",uri.Query);
  Assert.Contains("offset=40",uri.Query);
  Assert.Throws<ArgumentOutOfRangeException>(()=>FreelancerProjects.SearchUri(".NET",101,0));
 }

 [Theory]
 [InlineData(null,"token")]
 [InlineData("false","token")]
 [InlineData("true",null)]
 [InlineData("true","")]
 public async Task MissingPermissionOrOAuthTokenMakesZeroRequests(string? permission,string? token)
 {
  int calls=0;
  using var http=new HttpClient(new Handler(_=>{
   calls++;
   return Ok(JsonFixture);
  }));
  using var output=new StringWriter();
  await Assert.ThrowsAsync<InvalidOperationException>(()=>
   FreelancerProjects.ScanAsync(http,output,token,permission,["C#"],20,1,null,CancellationToken.None));
  Assert.Equal(0,calls);
  Assert.Equal("",output.ToString());
 }

 [Fact]
 public async Task AuthorizedScanMergesQueriesRanksProjectsAndDoesNotLeakOAuth()
 {
  var requests=new List<string>();
  using var http=new HttpClient(new Handler(req=>{
   Assert.Equal("test-oauth-secret",req.Headers.GetValues("Freelancer-OAuth-V1").Single());
   requests.Add(req.RequestUri!.AbsoluteUri);
   return Ok(JsonFixture);
  }));
  using var output=new StringWriter();
  var progress=new List<string>();
  var result=await FreelancerProjects.ScanAsync(http,output,"test-oauth-secret","true",
   ["C#", ".NET"],20,1,progress.Add,CancellationToken.None);
  Assert.Equal(2,result.ApiCalls);
  Assert.Equal(2,result.Queries);
  Assert.Equal(2,result.UniqueProjects);
  Assert.Single(result.Priority==1?new[]{1}:Array.Empty<int>());
  var rows=output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries);
  Assert.Equal(2,rows.Length);
  using var first=JsonDocument.Parse(rows[0]);
  Assert.Equal("Priority",first.RootElement.GetProperty("Bucket").GetString());
  Assert.Equal(2,first.RootElement.GetProperty("Queries").GetArrayLength());
  Assert.DoesNotContain("test-oauth-secret",output.ToString());
  Assert.Equal(2,progress.Count);
  Assert.Contains("C%23",requests[0]);
  Assert.Contains(".NET",Uri.UnescapeDataString(requests[1]));
 }

 [Theory]
 [InlineData(HttpStatusCode.Forbidden)]
 [InlineData(HttpStatusCode.Unauthorized)]
 [InlineData((HttpStatusCode)429)]
 public async Task DeniedOrThrottledResponseStopsWithoutRetryOrDataLeak(HttpStatusCode status)
 {
  int count=0;
  using var http=new HttpClient(new Handler(_=>{
   count++;
   return new HttpResponseMessage(status){Content=new StringContent("denied")};
  }));
  using var output=new StringWriter();
  var failure=await Assert.ThrowsAsync<InvalidDataException>(()=>
   FreelancerProjects.ScanAsync(http,output,"secret-token","true",
    [".NET"],20,2,null,CancellationToken.None));
  Assert.Equal(1,count);
  Assert.Equal("",output.ToString());
  Assert.DoesNotContain("secret-token",failure.Message);
 }

 [Fact]
 public async Task EmptyOrUnauthorizedApiDataFailsClosed()
 {
  using var http=new HttpClient(new Handler(_=>Ok("{}")));
  using var output=new StringWriter();
  await Assert.ThrowsAsync<InvalidDataException>(()=>
   FreelancerProjects.ScanAsync(http,output,"valid-token","true",
    [".NET"],20,1,null,CancellationToken.None));
  Assert.Empty(output.ToString());
 }

 private static HttpResponseMessage Ok(string json)=>
  new(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};

 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>
   Task.FromResult(respond(request));
 }
}
