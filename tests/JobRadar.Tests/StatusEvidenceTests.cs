using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

public sealed class StatusEvidenceTests
{
 [Theory]
 [InlineData("<html><body>Ця вакансія вже завершена</body></html>","html:standalone-closed-message")]
 [InlineData("<main><div role='alert'>No longer accepting applications</div><article>Build APIs</article></main>","html:explicit-closed-banner")]
 [InlineData("<div class='vacancy-status'>Вакансія закрита</div><article>Досвід .NET</article>","html:explicit-closed-banner")]
 public void ExplicitProviderClosureHasEvidence(string html,string expectedEvidence)
 {
  var observation=VacancyStatusEvidence.FromProviderHtml(html);
  Assert.False(observation.IsOpen);
  Assert.Equal(expectedEvidence,observation.EvidenceCode);
  Assert.False(Parsers.OpenStatus(html));
 }

 [Theory]
 [InlineData("<article><p>The product monitors when a job is closed. Build .NET APIs.</p></article>")]
 [InlineData("<footer>Position is closed</footer><main><article>Build .NET APIs.</article></main>")]
 [InlineData("<main><p>Remote. Build .NET APIs.</p><button>Apply now</button></main>")]
 [InlineData("<div role='alert'>Job is open for applications</div><article>Build .NET APIs.</article>")]
 [InlineData("<script type='application/ld+json'>{\"@type\":\"JobPosting\",\"validThrough\":\"2026-01-01\"}</script><article>Build .NET APIs</article>")]
 public void UnverifiedSourceDoesNotInferOpenOrClosed(string html)
 {
  var observation=VacancyStatusEvidence.FromProviderHtml(html);
  Assert.Null(observation.IsOpen);
  Assert.Equal("html:status-unverified",observation.EvidenceCode);
  Assert.Null(Parsers.OpenStatus(html));
 }

 [Fact]
 public async Task ProviderStatusHistoryRecordsClosedThenUnknownWithoutDeclaringOpen()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var id=Guid.NewGuid().ToString("N");
  var job=new JobRef("status-"+id,"https://jobs.dou.ua/vacancies/"+id,
    "Middle .NET Backend",null,DateTimeOffset.UtcNow);
  await storage.SaveDiscovered(job,CancellationToken.None);
  var t1=DateTimeOffset.UtcNow.AddMinutes(-2);
  await storage.Save(new JobDetail(job,new string('C',100),false,t1,
    "html:explicit-closed-banner"),CancellationToken.None);
  var closed=Assert.Single(await storage.ReadVacanciesForTriageAsync(50000,CancellationToken.None),
    x=>x.Url==job.Url);
  Assert.False(closed.IsOpen);
  Assert.Equal("html:explicit-closed-banner",closed.StatusEvidence);
  Assert.NotNull(closed.StatusCheckedAt);
  Assert.True((closed.StatusCheckedAt.Value-t1).Duration()<TimeSpan.FromMilliseconds(1));

  var t2=t1.AddMinutes(1);
  await storage.Save(new JobDetail(job,new string('U',100),null,t2,
    "html:status-unverified"),CancellationToken.None);
  var unknown=Assert.Single(await storage.ReadVacanciesForTriageAsync(50000,CancellationToken.None),
    x=>x.Url==job.Url);
  Assert.Null(unknown.IsOpen);
  Assert.Equal("html:status-unverified",unknown.StatusEvidence);
  Assert.NotNull(unknown.StatusCheckedAt);
  Assert.True((unknown.StatusCheckedAt.Value-t2).Duration()<TimeSpan.FromMilliseconds(1));
  Assert.Equal(FitBucket.NeedsReview,VacancyTriage.Assess(unknown).Bucket);

  await using var c=new NpgsqlConnection(cs);
  await c.OpenAsync();
  await using var cmd=new NpgsqlCommand("""
SELECT count(*),count(*) FILTER (WHERE open_status=false),
       count(*) FILTER (WHERE open_status IS NULL)
FROM job_status_checks WHERE source=@s AND url=@u
""",c);
  cmd.Parameters.AddWithValue("s",job.Source);
  cmd.Parameters.AddWithValue("u",job.Url);
  await using var reader=await cmd.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Equal(2L,reader.GetInt64(0));
  Assert.Equal(1L,reader.GetInt64(1));
  Assert.Equal(1L,reader.GetInt64(2));
 }

 [Fact]
 public async Task LateArrivingOldClosureDoesNotOverwriteNewerUnknownObservation()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var id=Guid.NewGuid().ToString("N");
  var job=new JobRef("ordering-"+id,"https://example.org/status/"+id,
   "Middle .NET Backend",null,null);
  await storage.SaveDiscovered(job,CancellationToken.None);
  var recent=DateTimeOffset.UtcNow;
  var older=recent.AddMinutes(-10);
  await storage.Save(new JobDetail(job,new string('A',100),null,recent,
   "html:status-unverified"),CancellationToken.None);
  await storage.Save(new JobDetail(job,new string('B',100),false,older,
   "html:explicit-closed-banner"),CancellationToken.None);
  var snapshot=Assert.Single(await storage.ReadVacanciesForTriageAsync(50000,CancellationToken.None),
   x=>x.Url==job.Url);
  Assert.Null(snapshot.IsOpen);
  Assert.Equal("html:status-unverified",snapshot.StatusEvidence);
  Assert.NotNull(snapshot.StatusCheckedAt);
  Assert.True((snapshot.StatusCheckedAt.Value-recent).Duration()<TimeSpan.FromMilliseconds(1));
 }

 [Fact]
 public async Task RobotaCompanyDescriptionDoesNotInventAStatusCheck()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var id=Guid.NewGuid().ToString("N");
  var job=new JobRef("robota-status-"+id,"https://example.org/"+id,
   ".NET Backend",null,null);
  await storage.SaveDiscovered(job,CancellationToken.None);
  await storage.Save(new JobDetail(job,new string('R',100),null,DateTimeOffset.UtcNow),
    CancellationToken.None);
  var snapshot=Assert.Single(await storage.ReadVacanciesForTriageAsync(50000,CancellationToken.None),
   x=>x.Url==job.Url);
  Assert.Null(snapshot.IsOpen);
  Assert.Null(snapshot.StatusCheckedAt);
  Assert.Null(snapshot.StatusEvidence);
 }
}
