using Npgsql;
using NpgsqlTypes;

namespace JobRadar;

/// <summary>
/// Durable, atomic UTC request reservation. The reservation is consumed even
/// when HTTP fails, ensuring JobRadar restarts and concurrent crawlers cannot
/// silently exceed the conservative Remotive budget.
/// </summary>
public sealed partial class Storage
{
 public async Task<bool> TryReserveRemotiveAsync(DateTimeOffset requestedAt,CancellationToken ct)
 {
  // Use UTC buckets, never wall-clock local dates or process memory.
  var moment=requestedAt.ToUniversalTime();
  var utcDay=DateOnly.FromDateTime(moment.UtcDateTime);
  const string sql="""
INSERT INTO remotive_daily_requests(day_utc,requests,last_requested_at)
SELECT @day,1,@at
WHERE NOT EXISTS (
 SELECT 1 FROM remotive_daily_requests
 WHERE last_requested_at > @at - INTERVAL '8 hours')
ON CONFLICT(day_utc) DO UPDATE SET
 requests=remotive_daily_requests.requests+1,
 last_requested_at=excluded.last_requested_at
WHERE remotive_daily_requests.requests<2
  AND remotive_daily_requests.last_requested_at<=
      excluded.last_requested_at - INTERVAL '8 hours'
  AND NOT EXISTS (
    SELECT 1 FROM remotive_daily_requests
    WHERE last_requested_at > excluded.last_requested_at - INTERVAL '8 hours')
RETURNING requests;
""";
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  // The unique UTC day row cannot serialize concurrent requests spanning
  // midnight. A transaction advisory lock makes the global eight-hour
  // cooldown atomic even for different date keys.
  await using(var guard=new NpgsqlCommand(
    "SELECT pg_advisory_xact_lock(837149246)",db,tx))
   await guard.ExecuteNonQueryAsync(ct);
  await using var command=new NpgsqlCommand(sql,db,tx);
  command.Parameters.AddWithValue("day",NpgsqlDbType.Date,utcDay);
  command.Parameters.AddWithValue("at",NpgsqlDbType.TimestampTz,moment);
  var reserved=await command.ExecuteScalarAsync(ct) is int;
  await tx.CommitAsync(ct);
  return reserved;
 }
}
