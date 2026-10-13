using Npgsql;
using NpgsqlTypes;

namespace JobRadar;

public sealed partial class Storage
{
 /// <summary>
 /// Recent DOU/Djinni URLs eligible for a separate bounded status check.
 /// Never interpret omission from an RSS feed as a closed vacancy.
 /// </summary>
 public async Task<IReadOnlyList<JobRef>> ReadStatusRefreshCandidatesAsync(
  int maxJobs,int minAgeHours,int recentDays,CancellationToken ct)
 {
  if(maxJobs is <1 or >30 || minAgeHours is <1 or >720 || recentDays is <1 or >90)
   throw new ArgumentOutOfRangeException(nameof(maxJobs));
  const string sql="""
SELECT source,url,title,company,published_at,preview
FROM jobs
WHERE source IN ('dou','djinni')
 AND last_seen >= now()-(@days * INTERVAL '1 day')
 AND (status_checked_at IS NULL OR
      status_checked_at < now()-(@hours * INTERVAL '1 hour'))
ORDER BY
 CASE WHEN open_status IS NULL THEN 0 ELSE 1 END,
 last_seen DESC, source, url
LIMIT @limit;
""";
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,db);
  cmd.Parameters.AddWithValue("days",recentDays);
  cmd.Parameters.AddWithValue("hours",minAgeHours);
  cmd.Parameters.AddWithValue("limit",maxJobs);
  var result=new List<JobRef>();
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   var published=reader.IsDBNull(4)?(DateTimeOffset?)null:
    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4),DateTimeKind.Utc));
   result.Add(new JobRef(reader.GetString(0),reader.GetString(1),
    reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),
    published,reader.IsDBNull(5)?null:reader.GetString(5)));
  }
  return result;
 }

 /// <summary>
 /// Append status evidence without touching vacancy title, description,
 /// full_text_at, last_seen, or discovery history. Monotonic timestamp and
 /// append-only audit record are transactional. A stale observation is ignored.
 /// </summary>
 public async Task<bool> SaveStatusOnlyAsync(
  JobRef job,VacancyStatusObservation observation,
  DateTimeOffset checkedAt,CancellationToken ct)
 {
  if(!StatusOnlyRefresh.IsApprovedProviderUrl(job.Source,job.Url))
   throw new ArgumentException("Unapproved status provider URL",nameof(job));
  if(observation.EvidenceCode is not
    ("html:status-unverified" or "html:explicit-closed-banner" or
     "html:standalone-closed-message" or "html:explicit-closed-heading" or
     "html:explicit-closed-title"))
   throw new ArgumentException("Unknown provider status evidence",nameof(observation));
  if((observation.IsOpen is null) !=
      (observation.EvidenceCode=="html:status-unverified") ||
     observation.IsOpen==true)
   throw new ArgumentException("Contradictory or unsupported provider status",nameof(observation));

  var when=checkedAt.ToUniversalTime();
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await using var cmd=new NpgsqlCommand("""
UPDATE jobs SET
 open_status=@open,
 status_checked_at=@checked,
 status_evidence=@evidence
WHERE source=@source AND url=@url
 AND (status_checked_at IS NULL OR status_checked_at<@checked);
""",db,tx);
  cmd.Parameters.AddWithValue("source",job.Source);
  cmd.Parameters.AddWithValue("url",job.Url);
  cmd.Parameters.AddWithValue("open",NpgsqlDbType.Boolean,
   (object?)observation.IsOpen??DBNull.Value);
  cmd.Parameters.AddWithValue("checked",NpgsqlDbType.TimestampTz,when);
  cmd.Parameters.AddWithValue("evidence",observation.EvidenceCode);
  var updated=await cmd.ExecuteNonQueryAsync(ct)>0;
  if(updated)
  {
   await using var history=new NpgsqlCommand("""
INSERT INTO job_status_checks(source,url,checked_at,open_status,evidence)
VALUES(@source,@url,@checked,@open,@evidence)
""",db,tx);
   history.Parameters.AddWithValue("source",job.Source);
   history.Parameters.AddWithValue("url",job.Url);
   history.Parameters.AddWithValue("checked",NpgsqlDbType.TimestampTz,when);
   history.Parameters.AddWithValue("open",NpgsqlDbType.Boolean,
    (object?)observation.IsOpen??DBNull.Value);
   history.Parameters.AddWithValue("evidence",observation.EvidenceCode);
   await history.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return updated;
 }
}
