using Npgsql;
using System.Text.Json;

namespace JobRadar;
public sealed partial class Storage
{
 /// <summary>
 /// Compare with the last 10 source reports. We read several prior crawl runs
 /// and use the largest valid recent count so a single anomalous low run does not reset the baseline.
 /// </summary>
 public async Task<IReadOnlyDictionary<string,int>> GetPreviousReferenceCountsAsync(CancellationToken ct)
 {
  var counts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(
   "SELECT report FROM crawl_runs ORDER BY id DESC LIMIT 10",c);
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   try
   {
    using var doc=JsonDocument.Parse(reader.GetString(0));
    if(!doc.RootElement.TryGetProperty("Sources",out var sources)
       || sources.ValueKind!=JsonValueKind.Array)continue;
    foreach(var item in sources.EnumerateArray())
    {
     if(!item.TryGetProperty("Source",out var name) || name.ValueKind!=JsonValueKind.String)
      continue;
     if(!item.TryGetProperty("ReferencesFound",out var n) ||
         !n.TryGetInt32(out var value) || value<0)continue;
     var source=name.GetString()!;
     if(item.TryGetProperty("Status",out var status) &&
        status.ToString()=="FAILED")continue;
     if(!counts.TryGetValue(source,out var previous) || value>previous)
      counts[source]=value;
    }
   }
   catch(JsonException) { /* outdated or malformed report cannot corrupt a crawl */ }
  }
  return counts;
 }
}
