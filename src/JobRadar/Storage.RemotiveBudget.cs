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
VALUES (@day,1,@at)
ON CONFLICT(day_utc) DO UPDATE SET
 requests=remotive_daily_requests.requests+1,
 last_requested_at=excluded.last_requested_at
WHERE remotive_daily_requests.requests<2
  AND excluded.last_requested_at >=
      remotive_daily_requests.last_requested_at + INTERVAL '8 hours'
RETURNING requests;
""";
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var command=new NpgsqlCommand(sql,db);
  command.Parameters.AddWithValue("day",NpgsqlDbType.Date,utcDay);
  command.Parameters.AddWithValue("at",NpgsqlDbType.TimestampTz,moment);
  return await command.ExecuteScalarAsync(ct) is int;
 }
}
