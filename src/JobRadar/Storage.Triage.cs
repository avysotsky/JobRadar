using Npgsql;
using System.Text.Json;

namespace JobRadar;

public sealed partial class Storage
{
 public async Task<long> CountVacanciesForTriageAsync(CancellationToken ct)
 {
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand("SELECT count(*) FROM jobs",db);
  return (long)(await cmd.ExecuteScalarAsync(ct)??0L);
 }

 public async Task<IReadOnlyList<VacancySnapshot>> ReadVacanciesForTriageAsync(
  int limit,CancellationToken ct)
 {
  if(limit is <1 or >50000)throw new ArgumentOutOfRangeException(nameof(limit));
  const string sql="""
SELECT j.source,j.url,j.title,j.company,j.preview,j.description,
       j.full_text_at,j.open_status,j.published_at,j.last_seen,
       COALESCE((SELECT json_agg(query_name ORDER BY query_name)::text FROM job_queries q
                WHERE q.source=j.source AND q.url=j.url),'[]'),
       j.status_checked_at,j.status_evidence
FROM jobs j
ORDER BY j.last_seen DESC,j.source,j.url LIMIT @limit;
""";
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,db);
  cmd.Parameters.AddWithValue("limit",limit);
  var result=new List<VacancySnapshot>();
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   DateTimeOffset? published=reader.IsDBNull(8)?null:
    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8),DateTimeKind.Utc));
   bool? opened=reader.IsDBNull(7)?null:reader.GetBoolean(7);
   result.Add(new VacancySnapshot(
    reader.GetString(0),reader.GetString(1),reader.GetString(2),
    reader.IsDBNull(3)?null:reader.GetString(3),
    reader.IsDBNull(4)?null:reader.GetString(4),
    reader.GetString(5),!reader.IsDBNull(6),opened,published,
    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(9),DateTimeKind.Utc)),
    JsonSerializer.Deserialize<string[]>(reader.GetString(10))??[],
    reader.IsDBNull(11)?null:
      new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(11),DateTimeKind.Utc)),
    reader.IsDBNull(12)?null:reader.GetString(12)));
  }
  return result;
 }
}
