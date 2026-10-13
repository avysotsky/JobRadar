using Npgsql;
using NpgsqlTypes;

namespace JobRadar;

public sealed partial class Storage
{
 /// <summary>
 /// Read only canonical, deduplicated discovery identities in one saved run.
 /// A hard LIMIT+1 makes accidental high-volume analysis fail closed.
 /// </summary>
 public async Task<IReadOnlyList<DiscoveryGapObservation>> ReadDiscoveryGapObservationsAsync(
  DateTimeOffset startedAt,DateTimeOffset endedAt,
  IReadOnlyList<string> queryNames,int maxRows,CancellationToken ct)
 {
  ArgumentNullException.ThrowIfNull(queryNames);
  if(startedAt>endedAt)throw new ArgumentOutOfRangeException(nameof(startedAt));
  if(maxRows is <1 or >50000)throw new ArgumentOutOfRangeException(nameof(maxRows));
  if(queryNames.Count>64)throw new ArgumentOutOfRangeException(nameof(queryNames));
  var names=queryNames.Where(x=>DiscoveryGapAudit.ProviderForQuery(x)!=null)
   .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  if(names.Length==0)return [];

  const string sql="""
SELECT DISTINCT source,query_name,url
FROM job_discoveries
WHERE observed_at>=@started AND observed_at<=@ended
  AND source IN ('dou','djinni','robota','wwr')
  AND query_name=ANY(@names)
ORDER BY source,query_name,url
LIMIT @max_plus_one;
""";
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,db);
  cmd.Parameters.AddWithValue("started",NpgsqlDbType.TimestampTz,startedAt.ToUniversalTime());
  cmd.Parameters.AddWithValue("ended",NpgsqlDbType.TimestampTz,endedAt.ToUniversalTime());
  cmd.Parameters.AddWithValue("names",NpgsqlDbType.Array|NpgsqlDbType.Text,names);
  cmd.Parameters.AddWithValue("max_plus_one",maxRows+1);
  var result=new List<DiscoveryGapObservation>();
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   if(result.Count==maxRows)
    throw new InvalidDataException("Discovery gap window exceeds safe row limit");
   result.Add(new DiscoveryGapObservation(reader.GetString(0),
    reader.GetString(1),reader.GetString(2)));
  }
  return result;
 }
}
