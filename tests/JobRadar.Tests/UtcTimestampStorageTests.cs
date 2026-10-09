using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

/// <summary>
/// PostgreSQL timestamptz requires UTC DateTimeOffset. Public vacancy feeds can
/// legitimately publish timestamps in Kyiv (+03:00) or other offsets.
/// </summary>
public sealed class UtcTimestampStorageTests
{
 [Theory]
 [InlineData(3)]
 [InlineData(-5)]
 public async Task NonUtcSourceDatesRoundTripAsTheSameUtcInstant(int offsetHours)
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;

  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var key=Guid.NewGuid().ToString("N");
  var source="test-tz-"+key;
  var url="https://example.invalid/job/"+key;
  var published=new DateTimeOffset(2026,10,9,12,30,0,TimeSpan.FromHours(offsetHours));
  var fetched=published.AddHours(2);
  var job=new JobRef(source,url,"Middle .NET Backend","Test Employer",published);

  // Both the initial discovery and the subsequent full-text/status writes
  // must accept nonzero offsets and preserve the represented UTC instant.
  await storage.SaveDiscovered(job,CancellationToken.None,"timezone-regression");
  await storage.Save(new JobDetail(job,new string('D',120),false,fetched,
    "test_explicit_provider_closure"),CancellationToken.None);

  await using var connection=new NpgsqlConnection(cs);
  await connection.OpenAsync();
  await using(var command=new NpgsqlCommand(
    "SELECT published_at,full_text_at,status_checked_at FROM jobs WHERE source=@s AND url=@u",
    connection))
  {
   command.Parameters.AddWithValue("s",source);
   command.Parameters.AddWithValue("u",url);
   await using var reader=await command.ExecuteReaderAsync();
   Assert.True(await reader.ReadAsync());
   Assert.Equal(published.UtcDateTime,reader.GetDateTime(0));
   Assert.Equal(fetched.UtcDateTime,reader.GetDateTime(1));
   Assert.Equal(fetched.UtcDateTime,reader.GetDateTime(2));
  }
  await using(var command=new NpgsqlCommand(
    "SELECT checked_at FROM job_status_checks WHERE source=@s AND url=@u",connection))
  {
   command.Parameters.AddWithValue("s",source);
   command.Parameters.AddWithValue("u",url);
   Assert.Equal(fetched.UtcDateTime,(DateTime)(await command.ExecuteScalarAsync())!);
  }

  // Audit reports must be safe even when timestamps originated outside UTC.
  await storage.SaveReport(new CrawlReport(published,fetched,[]),CancellationToken.None);
  var empty=new PendingCounts(0,0,0,0,0);
  await storage.SavePendingRetryReportAsync(
    new PendingRetryReport(published,fetched,0,0,0,empty,empty),
    CancellationToken.None);
 }
}
