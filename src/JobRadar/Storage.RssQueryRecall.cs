using Npgsql;
using NpgsqlTypes;

namespace JobRadar;

public sealed partial class Storage
{
 /// <summary>
 /// Load distinct canonical RSS reference occurrences observed within one
 /// persisted crawl window. Reads discovery history only: no provider calls,
 /// schema changes, or writes. Limit + 1 fails closed on oversized windows.
 /// </summary>
 public async Task<IReadOnlyList<RssQueryObservation>> ReadRssQueryObservationsAsync(
  DateTimeOffset startedAt,DateTimeOffset endedAt,
  IReadOnlyList<string> queryNames,int maxRows,CancellationToken ct)
 {
  if(startedAt>endedAt)throw new ArgumentOutOfRangeException(nameof(startedAt));
  if(maxRows is <1 or >50000)throw new ArgumentOutOfRangeException(nameof(maxRows));
  ArgumentNullException.ThrowIfNull(queryNames);
  if(queryNames.Count>64)throw new ArgumentOutOfRangeException(nameof(queryNames));
  var names=queryNames.Where(x=>!string.IsNullOrWhiteSpace(x) &&
   RssQueryRecall.CanonicalProvider(x)!=null)
   .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  if(names.Length==0)return [];
  const string sql="""
SELECT DISTINCT source,query_name,url
FROM job_discoveries
WHERE observed_at>=@start AND observed_at<=@end
  AND source IN ('dou','djinni')
  AND query_name=ANY(@names)
ORDER BY source,query_name,url
LIMIT @max_plus_one
""";
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,db);
  cmd.Parameters.AddWithValue("start",NpgsqlDbType.TimestampTz,startedAt.ToUniversalTime());
  cmd.Parameters.AddWithValue("end",NpgsqlDbType.TimestampTz,endedAt.ToUniversalTime());
  cmd.Parameters.AddWithValue("names",NpgsqlDbType.Array|NpgsqlDbType.Text,names);
  cmd.Parameters.AddWithValue("max_plus_one",maxRows+1);
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  var output=new List<RssQueryObservation>();
  while(await reader.ReadAsync(ct))
  {
   if(output.Count==maxRows)
    throw new InvalidDataException("RSS query discovery window exceeded safety limit");
   output.Add(new RssQueryObservation(reader.GetString(0),
    reader.GetString(1),reader.GetString(2)));
  }
  return output;
 }
}
