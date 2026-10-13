using System.Net;
using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

public sealed class PendingRetryTests
{
 private static RadarOptions Options()=>new()
  {EnabledRobota=false,EnabledDou=true,EnabledDjinni=false,EnabledWorkUa=false,
   PendingBatchSize=10,PendingMinimumAgeMinutes=0,PendingMaxAttempts=3};

 [Fact]
 public async Task RecoversVacancyNoLongerPresentInRssAndClearsError()
 {
  string? cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var job=new JobRef("dou","https://jobs.dou.ua/companies/test/vacancies/"+Random.Shared.Next(7000000,8000000)+"/",
    "Middle .NET Developer",null,null);
  await storage.SaveDiscovered(job,CancellationToken.None);
  await storage.SaveError(job.Source,job.Url,"Simulated network timeout",CancellationToken.None);
  var html="<article>"+new string('A',180)+"</article>";
  using var http=new HttpClient(new Stub(_=>new HttpResponseMessage(HttpStatusCode.OK)
   {Content=new StringContent(html)}));
  var runner=new PendingRetry(new HttpFetcher(http,0),storage,Options());
  var result=await runner.RunAsync(CancellationToken.None);
  // The test database is shared by other integration tests; retry may
  // also recover unrelated DOU jobs. Verify this specific vacancy below.
  Assert.True(result.Recovered>=1);
  await using var connection=new NpgsqlConnection(cs);
  await connection.OpenAsync();
  await using var cmd=new NpgsqlCommand(
    "SELECT length(j.description),count(e.url) FROM jobs j LEFT JOIN fetch_errors e ON e.source=j.source AND e.url=j.url WHERE j.source=@s AND j.url=@u GROUP BY j.description",connection);
  cmd.Parameters.AddWithValue("s",job.Source);cmd.Parameters.AddWithValue("u",job.Url);
  await using var reader=await cmd.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Equal(180,reader.GetInt32(0));
  Assert.Equal(0L,reader.GetInt64(1));
 }

 [Fact]
 public async Task Http403RemainsVisibleButIsNotRetried()
 {
  string? cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var job=new JobRef("dou","https://jobs.dou.ua/companies/blocked/vacancies/"+Random.Shared.Next(8000000,9000000)+"/",
    "Middle .NET Blocked",null,null);
  await storage.SaveDiscovered(job,CancellationToken.None);
  await storage.SaveError(job.Source,job.Url,"Non-retryable HTTP status 403 for "+job.Url,CancellationToken.None);
  // A shared test database may contain unrelated eligible DOU vacancies.
  // Counting ALL retry requests/recoveries makes this assertion order-dependent.
  // We must prove that this particular 403 vacancy is never replayed.
  var attemptedUrls=new List<string>();
  using var http=new HttpClient(new Stub(req=>{
   attemptedUrls.Add(req.RequestUri!.AbsoluteUri);
   return new HttpResponseMessage(HttpStatusCode.OK)
    {Content=new StringContent("<article>"+new string('X',180)+"</article>")};
  }));
  var runner=new PendingRetry(new HttpFetcher(http,0),storage,Options());
  var status=await runner.StatusAsync(CancellationToken.None);
  Assert.True(status.Blocked>=1);
  var eligible=await storage.GetPendingVacanciesAsync(
   ["dou"],Options().PendingMaxAttempts,Options().PendingMinimumAgeMinutes,
   100,CancellationToken.None);
  Assert.DoesNotContain(eligible,item=>item.Job.Url==job.Url);

  await runner.RunAsync(CancellationToken.None);
  Assert.DoesNotContain(job.Url,attemptedUrls);

  // Ensure the rejection remains persisted and no full text was fabricated,
  // regardless of legitimate retries of other vacancies in the shared DB.
  await using var connection=new NpgsqlConnection(cs);
  await connection.OpenAsync();
  await using var cmd=new NpgsqlCommand("""
SELECT f.attempts,f.last_error,j.full_text_at
FROM jobs j
JOIN fetch_errors f ON f.source=j.source AND f.url=j.url
WHERE j.source=@source AND j.url=@url
""",connection);
  cmd.Parameters.AddWithValue("source",job.Source);
  cmd.Parameters.AddWithValue("url",job.Url);
  await using var reader=await cmd.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Equal(1,reader.GetInt32(0));
  Assert.StartsWith("Non-retryable HTTP status 403",reader.GetString(1));
  Assert.True(reader.IsDBNull(2));
 }

 [Fact]
 public async Task RetryBudgetExhaustionIsReportedNotDeleted()
 {
  string? cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var job=new JobRef("dou","https://jobs.dou.ua/companies/exhausted/vacancies/"+Random.Shared.Next(9000000,9999999)+"/",
    "Middle .NET Exhausted",null,null);
  await storage.SaveDiscovered(job,CancellationToken.None);
  for(int i=0;i<3;i++)await storage.SaveError(job.Source,job.Url,"Simulated timeout",CancellationToken.None);
  var jobs=await storage.GetPendingVacanciesAsync(["dou"],3,0,50,CancellationToken.None);
  Assert.DoesNotContain(jobs,item=>item.Job.Url==job.Url);
  var state=await storage.GetPendingCountsAsync(["dou"],3,0,CancellationToken.None);
  Assert.True(state.Exhausted>=1);
 }

 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
   => Task.FromResult(respond(request));
 }
}
