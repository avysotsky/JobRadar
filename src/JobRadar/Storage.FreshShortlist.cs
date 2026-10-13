using Npgsql;
using NpgsqlTypes;

namespace JobRadar;

public sealed partial class Storage
{
 /// <summary>
 /// Return only first-recorded discovery dates within the operator's window.
 /// Earlier observations in job_discoveries disqualify a URL. Pre-Phase-15
 /// history is unknowable and is never reconstructed from last_seen.
 /// </summary>
 public async Task<IReadOnlyDictionary<(string Source,string Url),DateTimeOffset>>
  ReadFirstRecordedDiscoveriesAsync(DateTimeOffset since,DateTimeOffset now,
    int maxRows,CancellationToken ct)
 {
  if(since>now)throw new ArgumentOutOfRangeException(nameof(since));
  if(maxRows is <1 or >50000)throw new ArgumentOutOfRangeException(nameof(maxRows));
  const string sql="""
SELECT source,url,MIN(observed_at) AS first_seen
FROM job_discoveries
WHERE source IN ('dou','djinni','robota','wwr')
GROUP BY source,url
HAVING MIN(observed_at)>=@since AND MIN(observed_at)<=@now
ORDER BY first_seen DESC,source,url
LIMIT @limit_plus_one;
""";
  var result=new Dictionary<(string Source,string Url),DateTimeOffset>();
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,db);
  cmd.Parameters.AddWithValue("since",NpgsqlDbType.TimestampTz,since.ToUniversalTime());
  cmd.Parameters.AddWithValue("now",NpgsqlDbType.TimestampTz,now.ToUniversalTime());
  cmd.Parameters.AddWithValue("limit_plus_one",maxRows+1);
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   if(result.Count>=maxRows)
    throw new InvalidDataException("Recent discovery set exceeds safe audit limit");
   var utc=DateTime.SpecifyKind(reader.GetDateTime(2),DateTimeKind.Utc);
   result.Add((reader.GetString(0),reader.GetString(1)),new DateTimeOffset(utc));
  }
  return result;
 }
}
