using Npgsql;
using NpgsqlTypes;

namespace JobRadar;

/// <summary>Database-backed retry selection and audit of unresolved full-text jobs.</summary>
public sealed partial class Storage
{
 public async Task<PendingCounts> GetPendingCountsAsync(
  string[] sources,int maxAttempts,int ageMinutes,CancellationToken ct)
 {
  if(sources.Length==0)return new PendingCounts(0,0,0,0,0);
  const string sql="""
SELECT COUNT(*) AS total,
 COUNT(*) FILTER (WHERE COALESCE(f.attempts,0)<@max
  AND NOT (COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 401%' OR
           COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 403%' OR
           COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 404%')
  AND (f.last_attempt IS NULL OR f.last_attempt<=NOW()-(@age * INTERVAL '1 minute'))) AS ready,
 COUNT(*) FILTER (WHERE COALESCE(f.attempts,0)<@max
  AND NOT (COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 401%' OR
           COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 403%' OR
           COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 404%')
  AND f.last_attempt>NOW()-(@age * INTERVAL '1 minute')) AS deferred,
 COUNT(*) FILTER (WHERE
  COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 401%' OR
  COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 403%' OR
  COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 404%') AS blocked,
 COUNT(*) FILTER (WHERE COALESCE(f.attempts,0)>=@max
  AND NOT (COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 401%' OR
           COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 403%' OR
           COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 404%')) AS exhausted
FROM jobs j
LEFT JOIN fetch_errors f ON f.source=j.source AND f.url=j.url
WHERE j.full_text_at IS NULL AND j.source=ANY(@sources);
""";
  await using var c=new NpgsqlConnection(connectionString);
  await c.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,c);
  cmd.Parameters.AddWithValue("sources",NpgsqlDbType.Array|NpgsqlDbType.Text,sources);
  cmd.Parameters.AddWithValue("max",maxAttempts);
  cmd.Parameters.AddWithValue("age",ageMinutes);
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  if(!await reader.ReadAsync(ct))return new PendingCounts(0,0,0,0,0);
  int Count(int i)=>checked((int)reader.GetInt64(i));
  return new PendingCounts(Count(0),Count(1),Count(2),Count(3),Count(4));
 }

 public async Task<IReadOnlyList<PendingVacancy>> GetPendingVacanciesAsync(
  string[] sources,int maxAttempts,int ageMinutes,int limit,CancellationToken ct)
 {
  if(sources.Length==0)return [];
  const string sql="""
SELECT j.source,j.url,j.title,j.company,j.published_at,j.preview,
       COALESCE(f.attempts,0)
FROM jobs j LEFT JOIN fetch_errors f ON f.source=j.source AND f.url=j.url
WHERE j.full_text_at IS NULL AND j.source=ANY(@sources)
 AND COALESCE(f.attempts,0)<@max
 AND NOT (COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 401%' OR
          COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 403%' OR
          COALESCE(f.last_error,'') LIKE 'Non-retryable HTTP status 404%')
 AND (f.last_attempt IS NULL OR f.last_attempt<=NOW()-(@age * INTERVAL '1 minute'))
ORDER BY f.last_attempt ASC NULLS FIRST,j.last_seen ASC
LIMIT @limit;
""";
  await using var c=new NpgsqlConnection(connectionString);
  await c.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,c);
  cmd.Parameters.AddWithValue("sources",NpgsqlDbType.Array|NpgsqlDbType.Text,sources);
  cmd.Parameters.AddWithValue("max",maxAttempts);
  cmd.Parameters.AddWithValue("age",ageMinutes);
  cmd.Parameters.AddWithValue("limit",limit);
  var list=new List<PendingVacancy>();
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   DateTimeOffset? published=reader.IsDBNull(4)?null:
     new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4),DateTimeKind.Utc));
   var job=new JobRef(reader.GetString(0),reader.GetString(1),
      reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),
      published,reader.IsDBNull(5)?null:reader.GetString(5));
   list.Add(new PendingVacancy(job,reader.GetInt32(6)));
  }
  return list;
 }

 public async Task SavePendingRetryReportAsync(PendingRetryReport report,CancellationToken ct)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(
   "INSERT INTO retry_runs(started_at,ended_at,report) VALUES(@s,@e,@r)",c);
  cmd.Parameters.AddWithValue("s",report.StartedAt);
  cmd.Parameters.AddWithValue("e",report.EndedAt);
  cmd.Parameters.AddWithValue("r",NpgsqlDbType.Jsonb,
    System.Text.Json.JsonSerializer.Serialize(report));
  await cmd.ExecuteNonQueryAsync(ct);
 }
}
