using JobRadar;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace JobRadar.Tests;

public sealed class FreshShortlistStorageTests
{
 [Fact]
 public async Task FirstRecordedHistoryExcludesRediscoveredOldJobsAndAppliedOnes()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var db=new Storage(cs);
  await db.Initialize(CancellationToken.None);
  // Production DOU vacancy links use numeric identifiers.
  var id=Random.Shared.NextInt64(100000000,900000000);
  var recent=new JobRef("dou",$"https://jobs.dou.ua/vacancies/{id*10+1}/",
   "Middle .NET Backend",null,DateTimeOffset.UtcNow,
   "Fully remote ASP.NET Core REST API PostgreSQL");
  var old=new JobRef("dou",$"https://jobs.dou.ua/vacancies/{id*10+2}/",
   "Middle .NET Backend",null,DateTimeOffset.UtcNow,
   "Fully remote ASP.NET Core REST API PostgreSQL");
  var applied=new JobRef("dou",$"https://jobs.dou.ua/vacancies/{id*10+3}/",
   "Middle .NET Backend",null,DateTimeOffset.UtcNow,
   "Fully remote ASP.NET Core REST API PostgreSQL");
  foreach(var job in new[]{recent,old,applied})
   await db.SaveDiscovered(job,CancellationToken.None,"dou-rss-phase38");
  var now=DateTimeOffset.UtcNow.AddMinutes(2);
  await using(var connection=new NpgsqlConnection(cs))
  {
   await connection.OpenAsync();
   await using var command=new NpgsqlCommand("""
INSERT INTO job_discoveries(source,url,query_name,observed_at)
VALUES(@source,@url,@query,@historical)
""",connection);
   command.Parameters.AddWithValue("source","dou");
   command.Parameters.AddWithValue("url",old.Url);
   command.Parameters.AddWithValue("query","dou-rss-phase38");
   command.Parameters.AddWithValue("historical",NpgsqlDbType.TimestampTz,
    now.AddDays(-10).ToUniversalTime());
   await command.ExecuteNonQueryAsync();
  }
  await db.MarkEmploymentDecisionAsync(applied.Url,"Applied",CancellationToken.None);
  var first=await db.ReadFirstRecordedDiscoveriesAsync(
   now.AddDays(-7),now,50000,CancellationToken.None);
  Assert.Contains(("dou",recent.Url),first.Keys);
  Assert.Contains(("dou",applied.Url),first.Keys);
  Assert.DoesNotContain(("dou",old.Url),first.Keys);
  var snapshots=(await db.ReadVacanciesForTriageAsync(
   50000,CancellationToken.None)).Where(x=>x.Source=="dou" &&
    (x.Url==recent.Url||x.Url==old.Url||x.Url==applied.Url)).ToArray();
  var decisions=await db.ReadEmploymentDecisionsAsync(CancellationToken.None);
  var shortlist=FreshShortlist.Analyze(snapshots,first,decisions,3,now);
  Assert.Equal(2,shortlist.RecentRecordedJobs);
  Assert.Equal(1,shortlist.EligibleForReview);
  Assert.Equal(recent.Url,Assert.Single(shortlist.Vacancies).Url);
  await Assert.ThrowsAsync<InvalidDataException>(()=>
   db.ReadFirstRecordedDiscoveriesAsync(
    now.AddDays(-7),now,1,CancellationToken.None));
 }

 [Fact]
 public async Task InvalidBoundsRejectBeforeDatabaseConnection()
 {
  var db=new Storage("");
  var now=DateTimeOffset.UtcNow;
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
   db.ReadFirstRecordedDiscoveriesAsync(now.AddDays(1),now,1,CancellationToken.None));
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
   db.ReadFirstRecordedDiscoveriesAsync(now.AddDays(-7),now,0,CancellationToken.None));
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
   db.ReadFirstRecordedDiscoveriesAsync(now.AddDays(-7),now,50001,CancellationToken.None));
 }
}
