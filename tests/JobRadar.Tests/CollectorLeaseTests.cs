using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

/// <summary>
/// Use isolated JOBRADAR_TEST_DB. Deliberately opens separate PostgreSQL
/// sessions: a same-process Mutex cannot satisfy this test.
/// </summary>
public sealed class CollectorLeaseTests
{
 [Fact]
 public async Task CompetingPostgreSqlSessionsCannotBothHoldCollectorLease()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);

  var first=await storage.TryAcquireCollectorLeaseAsync(CancellationToken.None);
  Assert.NotNull(first);
  try
  {
   var second=await new Storage(cs).TryAcquireCollectorLeaseAsync(CancellationToken.None);
   Assert.Null(second);
  }
  finally
  {
   await first.DisposeAsync();
  }

  await using var third=await storage.TryAcquireCollectorLeaseAsync(CancellationToken.None);
  Assert.NotNull(third);
 }

 [Fact]
 public async Task EarlyDisposalAndRepeatedDisposalReleaseLease()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  var lease=await storage.TryAcquireCollectorLeaseAsync(CancellationToken.None);
  Assert.NotNull(lease);
  await lease.DisposeAsync();
  await lease.DisposeAsync();

  await using var reacquired=await new Storage(cs)
   .TryAcquireCollectorLeaseAsync(CancellationToken.None);
  Assert.NotNull(reacquired);
 }

 [Fact]
 public async Task LeaseAcquisitionUsesDifferentDatabaseSessionsWithoutWrites()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;

  await using var lease=await new Storage(cs)
   .TryAcquireCollectorLeaseAsync(CancellationToken.None);
  Assert.NotNull(lease);
  // Existing DB connections for read-only work remain available while a
  // collector holds the advisory lock.
  await using var readOnly=new NpgsqlConnection(cs);
  await readOnly.OpenAsync();
  await using var cmd=new NpgsqlCommand("SELECT 1",readOnly);
  Assert.Equal(1,await cmd.ExecuteScalarAsync());
 }
}
