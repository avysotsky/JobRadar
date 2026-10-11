using Npgsql;

namespace JobRadar;

/// <summary>
/// A PostgreSQL session-level, non-blocking lock shared by JobRadar processes
/// targeting the same PostgreSQL database. Keep the dedicated connection open
/// throughout the entire crawl/retry operation. Never pool a session holding
/// an advisory lock.
/// </summary>
public sealed class CollectorLease : IAsyncDisposable
{
 internal const long AdvisoryKey=815729004641387761L;
 private NpgsqlConnection? connection;

 private CollectorLease(NpgsqlConnection acquiredConnection) =>
  connection=acquiredConnection;

 public static async Task<CollectorLease?> TryAcquireAsync(
  string connectionString,CancellationToken ct)
 {
  // Pooled connections can outlive a lease holder, so use a dedicated
  // physical session that closes deterministically on IAsyncDispose.
  var builder=new NpgsqlConnectionStringBuilder(connectionString)
  {
   Pooling=false
  };
  var connection=new NpgsqlConnection(builder.ConnectionString);
  try
  {
   await connection.OpenAsync(ct);
   await using var command=new NpgsqlCommand(
    "SELECT pg_try_advisory_lock(@lease_key)",connection);
   command.Parameters.AddWithValue("lease_key",AdvisoryKey);
   var acquired=await command.ExecuteScalarAsync(ct);
   if(acquired is not true)
   {
    await connection.DisposeAsync();
    return null;
   }
   return new CollectorLease(connection);
  }
  catch
  {
   await connection.DisposeAsync();
   throw;
  }
 }

 public async ValueTask DisposeAsync()
 {
  var held=Interlocked.Exchange(ref connection,null);
  if(held is null)return;
  // The connection is unpooled: closing it releases every advisory lock held
  // by this session even if the explicit release command fails.
  try
  {
   if(held.State==System.Data.ConnectionState.Open)
   {
    await using var unlock=new NpgsqlCommand(
     "SELECT pg_advisory_unlock(@lease_key)",held);
    unlock.Parameters.AddWithValue("lease_key",AdvisoryKey);
    await unlock.ExecuteScalarAsync();
   }
  }
  finally
  {
   await held.DisposeAsync();
  }
 }
}

public sealed partial class Storage
{
 public Task<CollectorLease?> TryAcquireCollectorLeaseAsync(CancellationToken ct)=>
  CollectorLease.TryAcquireAsync(connectionString,ct);
}
