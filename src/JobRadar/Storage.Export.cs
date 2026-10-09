using System.Text.Json;
using Npgsql;

namespace JobRadar;

public sealed partial class Storage
{
 /// <summary>
 /// Stream canonical records as JSON Lines for offline analysis.
 /// A null FullTextAt means only the preview is available.
 /// </summary>
 public async Task<int> ExportJobsJsonlAsync(TextWriter writer,int limit,CancellationToken ct)
 {
  if(limit<1 || limit>50000)throw new ArgumentOutOfRangeException(nameof(limit));
  const string sql="""
SELECT j.source,j.url,j.title,j.company,j.published_at,j.preview,j.description,
       j.full_text_at,j.last_seen,j.open_status,j.status_checked_at,j.status_evidence,
       COALESCE((
        SELECT json_agg(query_name ORDER BY query_name)::text
        FROM job_queries q WHERE q.source=j.source AND q.url=j.url
       ),'[]') AS discovery_queries
FROM jobs j
ORDER BY j.last_seen DESC,j.source,j.url
LIMIT @limit
""";
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,c);
  cmd.Parameters.AddWithValue("limit",limit);
  int count=0;
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   var published=reader.IsDBNull(4)?(DateTime?)null:reader.GetDateTime(4);
   var fetched=reader.IsDBNull(7)?(DateTime?)null:reader.GetDateTime(7);
   var isOpen=reader.IsDBNull(9)?(bool?)null:reader.GetBoolean(9);
   var record=new{
    Source=reader.GetString(0),Url=reader.GetString(1),
    Title=reader.GetString(2),Company=reader.IsDBNull(3)?null:reader.GetString(3),
    PublishedAt=published,
    Preview=reader.IsDBNull(5)?null:reader.GetString(5),
    Description=reader.GetString(6),
    HasFullText=fetched.HasValue,
    FullTextAt=fetched,
    LastSeen=reader.GetDateTime(8),
    OpenStatus=isOpen,
    StatusCheckedAt=reader.IsDBNull(10)?(DateTime?)null:reader.GetDateTime(10),
    StatusEvidence=reader.IsDBNull(11)?null:reader.GetString(11),
    Queries=JsonSerializer.Deserialize<string[]>(reader.GetString(12))??[]
   };
   await writer.WriteLineAsync(JsonSerializer.Serialize(record).AsMemory(),ct);
   count++;
  }
  return count;
 }
}
