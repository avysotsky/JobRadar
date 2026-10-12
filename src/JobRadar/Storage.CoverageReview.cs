using System.Text.Json;
using Npgsql;

namespace JobRadar;

public sealed partial class Storage
{
 /// <summary>
 /// Read a bounded recent history for offline coverage analysis only.
 /// Insertion order, not arbitrary report timestamps, defines newest run.
 /// A corrupt persisted report must not silently disappear from the audit.
 /// </summary>
 public async Task<IReadOnlyList<CrawlReport>> ReadRecentCrawlReportsAsync(
  int limit,CancellationToken ct)
 {
  if(limit is <1 or >100)throw new ArgumentOutOfRangeException(nameof(limit));
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var command=new NpgsqlCommand(
   "SELECT report::text FROM crawl_runs ORDER BY id DESC LIMIT @limit",db);
  command.Parameters.AddWithValue("limit",limit);
  await using var reader=await command.ExecuteReaderAsync(ct);
  var runs=new List<CrawlReport>();
  while(await reader.ReadAsync(ct))
  {
   CrawlReport? report;
   try{report=JsonSerializer.Deserialize<CrawlReport>(reader.GetString(0));}
   catch(JsonException e)
   {
    throw new InvalidDataException("Persisted crawl report is malformed",e);
   }
   if(report?.Sources is null)
    throw new InvalidDataException("Persisted crawl report has no Sources array");
   runs.Add(report);
  }
  return runs;
 }
}
