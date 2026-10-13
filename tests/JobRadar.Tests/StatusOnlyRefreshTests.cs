using System.Net;
using System.Text;
using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

public sealed class StatusOnlyRefreshTests
{
 [Theory]
 [InlineData("dou","https://jobs.dou.ua/companies/solar/vacancies/123/")]
 [InlineData("dou","https://jobs.dou.ua/vacancies/123/")]
 [InlineData("djinni","https://djinni.co/jobs/123-middle-net-developer/")]
 [InlineData("djinni","https://www.djinni.co/jobs/123/")]
 public void KnownProviderJobUrlsAreAllowed(string source,string url)
 {
  Assert.True(StatusOnlyRefresh.IsApprovedProviderUrl(source,url));
 }

 [Theory]
 [InlineData("robota","https://jobs.dou.ua/vacancies/123/")]
 [InlineData("dou","http://jobs.dou.ua/vacancies/123/")]
 [InlineData("dou","https://jobs.dou.ua.evil.invalid/vacancies/123/")]
 [InlineData("dou","https://jobs.dou.ua:8443/vacancies/123/")]
 [InlineData("dou","https://jobs.dou.ua/vacancies/123?redirect=example")]
 [InlineData("djinni","https://djinni.co/jobs/rss/")]
 [InlineData("djinni","https://djinni.co/jobs/123#fragment")]
 [InlineData("dou","https://jobs.dou.ua/other-page")]
 public void RejectsUnexpectedSourcesUrlsPortsAndTracking(string source,string url)
 {
  Assert.False(StatusOnlyRefresh.IsApprovedProviderUrl(source,url));
 }

 [Fact]
 public async Task StatusOnlyUpdatePreservesDescriptionDiscoveryAndHistoryOrdering()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var token=DateTime.UtcNow.Ticks.ToString();
  var url="https://jobs.dou.ua/vacancies/"+token+"/";
  var refJob=new JobRef("dou",url,"Middle .NET Backend",null,DateTimeOffset.UtcNow);
  var text="Fully remote ASP.NET Core, EF Core, PostgreSQL "+new string('X',100);
  await storage.SaveDiscovered(refJob,CancellationToken.None,"dou");
  var initial=DateTimeOffset.UtcNow.AddMinutes(-3);
  await storage.Save(new JobDetail(refJob,text,null,initial,
    "html:status-unverified"),CancellationToken.None);
  var before=Assert.Single(await storage.ReadVacanciesForTriageAsync(50000,CancellationToken.None),
    x=>x.Url==url);
  var changedAt=DateTimeOffset.UtcNow;
  var closed=new VacancyStatusObservation(false,"html:explicit-closed-heading");
  Assert.True(await storage.SaveStatusOnlyAsync(refJob,closed,changedAt,CancellationToken.None));
  var after=Assert.Single(await storage.ReadVacanciesForTriageAsync(50000,CancellationToken.None),
    x=>x.Url==url);
  Assert.Equal(before.Description,after.Description);
  Assert.Equal(before.LastSeen,after.LastSeen);
  Assert.Equal(before.Title,after.Title);
  Assert.Equal(before.PublishedAt,after.PublishedAt);
  Assert.Equal(before.Queries,after.Queries);
  Assert.False(after.IsOpen);
  Assert.Equal("html:explicit-closed-heading",after.StatusEvidence);
  Assert.Equal(FitBucket.Excluded,VacancyTriage.Assess(after).Bucket);

  var staleTime=changedAt.AddSeconds(-1);
  Assert.False(await storage.SaveStatusOnlyAsync(refJob,
   new VacancyStatusObservation(null,"html:status-unverified"),
   staleTime,CancellationToken.None));
  var unchanged=Assert.Single(await storage.ReadVacanciesForTriageAsync(50000,CancellationToken.None),
    x=>x.Url==url);
  Assert.False(unchanged.IsOpen);

  using var db=new NpgsqlConnection(cs);
  await db.OpenAsync();
  using var cmd=new NpgsqlCommand("""
SELECT count(*),count(*) FILTER (WHERE open_status=false)
FROM job_status_checks WHERE source='dou' AND url=@url
""",db);
  cmd.Parameters.AddWithValue("url",url);
  await using var rows=await cmd.ExecuteReaderAsync();
  Assert.True(await rows.ReadAsync());
  Assert.Equal(2L,rows.GetInt64(0));
  Assert.Equal(1L,rows.GetInt64(1));
 }

 [Fact]
 public async Task UnknownStatusDoesNotBecomeOpenFromHtmlSuccess()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var url="https://djinni.co/jobs/"+DateTime.UtcNow.Ticks+"/";
  var job=new JobRef("djinni",url,"Middle .NET Backend",null,null);
  await storage.SaveDiscovered(job,CancellationToken.None);
  var html="<html><main><h1>Middle .NET Backend</h1>"+
   "<article>ASP.NET Core remote role.</article><button>Apply now</button></main></html>";
  var observed=VacancyStatusEvidence.FromProviderHtml(html);
  Assert.Null(observed.IsOpen);
  Assert.True(await storage.SaveStatusOnlyAsync(
   job,observed,DateTimeOffset.UtcNow,CancellationToken.None));
  var snapshot=Assert.Single(await storage.ReadVacanciesForTriageAsync(
   50000,CancellationToken.None),x=>x.Url==url);
  Assert.Null(snapshot.IsOpen);
  Assert.Equal("html:status-unverified",snapshot.StatusEvidence);
  Assert.False(snapshot.HasFullText);
 }

 [Fact]
 public async Task InvalidPositiveOpenAssertionAndUnknownEvidenceAreRejected()
 {
  var storage=new Storage("");
  var job=new JobRef("dou","https://jobs.dou.ua/vacancies/123/",".NET",null,null);
  await Assert.ThrowsAsync<ArgumentException>(()=>storage.SaveStatusOnlyAsync(job,
   new VacancyStatusObservation(true,"html:status-unverified"),
   DateTimeOffset.UtcNow,CancellationToken.None));
  await Assert.ThrowsAsync<ArgumentException>(()=>storage.SaveStatusOnlyAsync(job,
   new VacancyStatusObservation(false,"test:fake-closed"),
   DateTimeOffset.UtcNow,CancellationToken.None));
 }

 [Fact]
 public async Task NoRedirectsAreFollowedDuringStatusCheck()
 {
  int requests=0;
  using var handler=new Responder(req=>{
   requests++;
   return new HttpResponseMessage(HttpStatusCode.Found)
    {Headers={Location=new Uri("https://another.invalid/other")}};
  });
  using var client=new HttpClient(handler);
  var fetch=new HttpFetcher(client,0);
  await Assert.ThrowsAnyAsync<Exception>(()=>fetch.GetAsync(
   "https://jobs.dou.ua/vacancies/123/",CancellationToken.None));
  Assert.Equal(1,requests);
 }

 private sealed class Responder(Func<HttpRequestMessage,HttpResponseMessage> reply):
  HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(
   HttpRequestMessage request,CancellationToken cancellationToken)=>
   Task.FromResult(reply(request));
 }
}
