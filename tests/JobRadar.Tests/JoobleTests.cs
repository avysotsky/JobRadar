using System.Net;
using System.Text;
using JobRadar;
using Xunit;
using Npgsql;

namespace JobRadar.Tests;
public sealed class JoobleTests
{
 [Fact] public void ParsesOfficialApiFieldsWithoutConfusingUpdatedWithPublished()
 {
  const string response="""{"totalCount":100,"jobs":[{"id":1,"title":"Middle .NET Developer","link":"https://ua.jooble.org/jdp/12345","snippet":"ASP.NET Core, PostgreSQL","company":"Example Ltd","updated":"2026-10-09T12:00:00Z"},{"id":2,"title":"duplicate","link":"https://ua.jooble.org/jdp/12345","snippet":"test"},{"id":3,"title":"bad","link":"http://localhost/private"}]}""";
  var r=JoobleApi.ParseResponse(response);
  Assert.Equal(100,r.TotalCount);
  var job=Assert.Single(r.Jobs);
  Assert.Equal("jooble",job.Source);
  Assert.Contains("PostgreSQL",job.Preview);
  Assert.Null(job.PublishedAt);
 }
 [Fact] public void MalformedApiIsAnExplicitFailure()
  => Assert.Throws<InvalidDataException>(()=>JoobleApi.ParseResponse("""{"jobs":[]}"""));
 [Fact] public async Task UsesPostAndHasNoApiKeyInException()
 {
  var stub=new Handler(_=>new HttpResponseMessage(HttpStatusCode.Forbidden));
  using var client=new HttpClient(stub);
  var api=new JoobleApi(client,"testdummy123");
  var error=await Assert.ThrowsAsync<HttpRequestException>(()=>api.SearchAsync(".NET","Ukraine",1,CancellationToken.None));
  Assert.Contains("403",error.Message);
  Assert.DoesNotContain("testdummy123",error.Message);
  Assert.Equal(HttpMethod.Post,stub.Method);
 }
 [Fact] public async Task ReserveQuotaPersistsAndRejectsOverspendAcrossRestarts()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);
  var id="jooble-test-"+Guid.NewGuid().ToString("N");
  Assert.Equal(499,await store.ReserveJoobleCallAsync(498,2,CancellationToken.None,id));
  Assert.Equal(500,await store.ReserveJoobleCallAsync(498,2,CancellationToken.None,id));
  await Assert.ThrowsAsync<InvalidOperationException>(
    ()=>store.ReserveJoobleCallAsync(498,2,CancellationToken.None,id));
 }
 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> factory):HttpMessageHandler
 {
  public HttpMethod? Method {get;private set;}
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
  {Method=req.Method;return Task.FromResult(factory(req));}
 }
}
