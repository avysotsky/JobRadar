using Npgsql;

namespace JobRadar;

public sealed partial class Storage
{
 /// <summary>Skip repeated full-text HTTP requests for recent descriptions,
 /// while preserving the new query's discovery provenance.</summary>
 public async Task<bool> HasRecentFullTextAsync(JobRef job,int freshnessHours,CancellationToken ct)
 {
  if(freshnessHours<1)throw new ArgumentOutOfRangeException(nameof(freshnessHours));
  await using var connection=new NpgsqlConnection(connectionString);
  await connection.OpenAsync(ct);
  await using var command=new NpgsqlCommand("""
SELECT EXISTS(SELECT 1 FROM jobs
 WHERE source=@source AND url=@url
 AND full_text_at>=NOW()-(@hours * INTERVAL '1 hour')
 AND length(description)>=80)
""",connection);
  command.Parameters.AddWithValue("source",job.Source);
  command.Parameters.AddWithValue("url",job.Url);
  command.Parameters.AddWithValue("hours",freshnessHours);
  return await command.ExecuteScalarAsync(ct) is true;
 }
}
