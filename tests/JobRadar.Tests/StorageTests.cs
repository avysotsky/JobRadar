using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;
public sealed class StorageTests
{
 [Fact]
 public async Task DiscoverySurvivesDetailFailureAndFullFetchUpgradesIt()
 {
  // CI sets this to its disposable postgres service; developer machines may omit it.
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrEmpty(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var unique=Guid.NewGuid().ToString("N");
  var job=new JobRef("test-"+unique,"https://robota.ua/company307419/vacancy11287397",
      "Backend Engineer (.NET)", "КРЕДІ АГРІКОЛЬ БАНК",
      new DateTimeOffset(2026,10,5,9,0,0,TimeSpan.Zero),"Partial preview only");
  await storage.SaveDiscovered(job,CancellationToken.None);
  await storage.SaveError(job.Source,job.Url,"Simulated HTTP 403",CancellationToken.None);
  await using var connection=new NpgsqlConnection(cs);
  await connection.OpenAsync();
  await using(var command=new NpgsqlCommand(
      "SELECT preview,description,full_text_at FROM jobs WHERE source=@s AND url=@u",connection))
  {
   command.Parameters.AddWithValue("s",job.Source);
   command.Parameters.AddWithValue("u",job.Url);
   await using var reader=await command.ExecuteReaderAsync();
   Assert.True(await reader.ReadAsync());
   Assert.Equal("Partial preview only",reader.GetString(0));
   Assert.Equal("",reader.GetString(1));
   Assert.True(reader.IsDBNull(2));
  }
  await storage.Save(new JobDetail(job,new string('x',180),null,DateTimeOffset.UtcNow),CancellationToken.None);
  await using(var command=new NpgsqlCommand(
     "SELECT length(description),full_text_at IS NOT NULL FROM jobs WHERE source=@s AND url=@u",connection))
  {
   command.Parameters.AddWithValue("s",job.Source);
   command.Parameters.AddWithValue("u",job.Url);
   await using var reader=await command.ExecuteReaderAsync();
   Assert.True(await reader.ReadAsync());
   Assert.Equal(180,reader.GetInt32(0));
   Assert.True(reader.GetBoolean(1));
  }
  await using(var command=new NpgsqlCommand("SELECT count(*) FROM fetch_errors WHERE source=@s",connection))
  {
   command.Parameters.AddWithValue("s",job.Source);
   Assert.Equal(0L,await command.ExecuteScalarAsync());
  }
 }
}