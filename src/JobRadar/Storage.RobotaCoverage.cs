using System.Text.Json;
using Npgsql;

namespace JobRadar;

public sealed partial class Storage
{
 /// <summary>
 /// Fetch only the latest saved crawl report for read-only coverage analysis.
 /// Does not touch provider endpoints or mutate PostgreSQL.
 /// </summary>
 public async Task<CrawlReport?> ReadLatestCrawlReportAsync(CancellationToken ct)
 {
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(
   "SELECT report::text FROM crawl_runs ORDER BY id DESC LIMIT 1",db);
  var raw=await cmd.ExecuteScalarAsync(ct);
  return raw is string json?JsonSerializer.Deserialize<CrawlReport>(json):null;
 }
}
