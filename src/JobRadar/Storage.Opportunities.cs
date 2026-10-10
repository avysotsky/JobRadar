using System.Text.Json;
using Npgsql;

namespace JobRadar;

/// <summary>
/// Starts with Phase 15 observations. Historic runs predating the new event
/// table cannot be reconstructed; missing history is not evidence of one visit.
/// </summary>
public sealed record JobDiscoveryHistory(
 DateTimeOffset FirstObservedAt,DateTimeOffset LastObservedAt,long Observations);

public sealed record StoredSourceCoverage(
 string Source,DateTimeOffset ScanEndedAt,string Status,
 int ReferencesFound,int DetailsFetched,int DetailsFailed,
 int? ReportedTotal,string? Warning,string? Error);

public sealed partial class Storage
{
 public async Task<IReadOnlyDictionary<(string Source,string Url),JobDiscoveryHistory>>
  ReadDiscoveryHistoriesAsync(CancellationToken ct)
 {
  const string sql="""
SELECT source,url,min(observed_at),max(observed_at),count(*)
FROM job_discoveries
GROUP BY source,url
ORDER BY source,url
""";
  var result=new Dictionary<(string Source,string Url),JobDiscoveryHistory>();
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,db);
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   var first=new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2),DateTimeKind.Utc));
   var last=new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3),DateTimeKind.Utc));
   result[(reader.GetString(0),reader.GetString(1))]=
    new JobDiscoveryHistory(first,last,reader.GetInt64(4));
  }
  return result;
 }

 /// <summary>
 /// Provider-reported crawl coverage, NOT a whole-board completeness guarantee.
 /// Returns the most recent result for each source from stored crawl reports.
 /// </summary>
 public async Task<IReadOnlyList<StoredSourceCoverage>> ReadLatestCoverageAsync(
  int recentRuns,CancellationToken ct)
 {
  if(recentRuns is <1 or >100)throw new ArgumentOutOfRangeException(nameof(recentRuns));
  var results=new Dictionary<string,StoredSourceCoverage>(StringComparer.OrdinalIgnoreCase);
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(
    "SELECT report::text FROM crawl_runs ORDER BY id DESC LIMIT @limit",db);
  cmd.Parameters.AddWithValue("limit",recentRuns);
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   var report=JsonSerializer.Deserialize<CrawlReport>(reader.GetString(0));
   if(report is null)continue;
   foreach(var source in report.Sources)
   {
    if(results.ContainsKey(source.Source))continue;
    results.Add(source.Source,new StoredSourceCoverage(
     source.Source,report.EndedAt,source.Status,source.ReferencesFound,
     source.DetailsFetched,source.DetailsFailed,source.ReportedTotal,
     source.CoverageWarning,source.Error));
   }
  }
  return results.Values.OrderBy(x=>x.Source,StringComparer.OrdinalIgnoreCase).ToArray();
 }
}
