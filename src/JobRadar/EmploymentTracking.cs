using Npgsql;
using NpgsqlTypes;

namespace JobRadar;

/// <summary>Canonical URLs and user-entered employment actions. Never submits applications.</summary>
public static class EmploymentTrackingPolicy
{
 public static string Url(string? raw)
 {
  if(!Uri.TryCreate(raw?.Trim(),UriKind.Absolute,out var uri)||
     uri.Scheme!=Uri.UriSchemeHttps || !uri.IsDefaultPort ||
     !string.IsNullOrEmpty(uri.UserInfo))
   throw new ArgumentException("Employment URL must be HTTPS without credentials or custom ports");
  var host=uri.Host.ToLowerInvariant();
  if(host.StartsWith("www.",StringComparison.Ordinal))host=host[4..];
  var path=uri.AbsolutePath;
  bool accepted=host switch
  {
   "jobs.dou.ua"=>path.StartsWith("/companies/",StringComparison.OrdinalIgnoreCase)
      && path.Contains("/vacancies/",StringComparison.OrdinalIgnoreCase),
   "djinni.co"=>path.StartsWith("/jobs/",StringComparison.OrdinalIgnoreCase)
      && path.Length>"/jobs/".Length,
   "robota.ua"=>path.Contains("/vacancy",StringComparison.OrdinalIgnoreCase)
      && path.Length>"/vacancy".Length,
   "weworkremotely.com"=>path.StartsWith("/remote-jobs/",StringComparison.OrdinalIgnoreCase)
      && path.Length>"/remote-jobs/".Length,
   "remotive.com"=>path.StartsWith("/remote-jobs/",StringComparison.OrdinalIgnoreCase)
      && path.Length>"/remote-jobs/".Length,
   _=>false
  };
  if(!accepted)throw new ArgumentException("Unsupported employment provider or vacancy path");
  // Never keep URL query tokens, UTM tracking, credentials or fragment.
  var clean=new UriBuilder(uri){Host=host,Query="",Fragment=""}
      .Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
  if(clean.Length>1500)throw new ArgumentException("Employment URL too long");
  return clean;
 }
 public static string? Label(string? raw)
 {
  if(raw is null)return null;
  var value=raw.Trim();
  if(value.Length>180 || value.Any(char.IsControl))
   throw new ArgumentException("Employment label must be <=180 printable characters");
  return value.Length==0?null:value;
 }
 public static string? Note(string? raw)
 {
  if(raw is null)return null;
  var value=raw.Trim();
  if(value.Length>500 || value.Any(char.IsControl))
   throw new ArgumentException("Employment note must be <=500 printable characters");
  return value.Length==0?null:value;
 }
 public static string Event(string? raw)
 {
  var value=(raw??"").Trim();
  if(value is not ("Applied" or "Replied" or "Interview" or "Offer" or
                   "Rejected" or "Withdrawn" or "Closed"))
   throw new ArgumentException("Unsupported employment event");
  return value;
 }
 public static bool TryUrl(string? raw,out string? canonical)
 {
  try {canonical=Url(raw);return true;}
  catch(ArgumentException) {canonical=null;return false;}
 }
}

public sealed record TrackedEmployment(
 string Url,string? Label,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,
 string CurrentStatus,DateTimeOffset? AppliedAt,long EventCount);

public sealed partial class Storage
{
 public async Task SaveManualEmploymentAsync(string url,string? label,CancellationToken ct)
 {
  url=EmploymentTrackingPolicy.Url(url);
  label=EmploymentTrackingPolicy.Label(label);
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand("""
INSERT INTO employment_tracker(url,label) VALUES(@u,@l)
ON CONFLICT(url) DO UPDATE SET
 label=COALESCE(EXCLUDED.label,employment_tracker.label),
 updated_at=now()
""",db);
  cmd.Parameters.AddWithValue("u",url);
  cmd.Parameters.AddWithValue("l",NpgsqlDbType.Text,(object?)label??DBNull.Value);
  await cmd.ExecuteNonQueryAsync(ct);
 }
 public async Task RecordOwnEmploymentEventAsync(
  string url,string eventType,string? ownNote,CancellationToken ct)
 {
  url=EmploymentTrackingPolicy.Url(url);
  eventType=EmploymentTrackingPolicy.Event(eventType);
  ownNote=EmploymentTrackingPolicy.Note(ownNote);
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await using(var lockCmd=new NpgsqlCommand(
    "SELECT 1 FROM employment_tracker WHERE url=@u FOR UPDATE",db,tx))
  {
   lockCmd.Parameters.AddWithValue("u",url);
   if(await lockCmd.ExecuteScalarAsync(ct) is null)
    throw new InvalidOperationException("Save the employment reference before recording an event");
  }
  if(eventType is "Replied" or "Interview" or "Offer" or "Rejected" or "Withdrawn")
  {
   await using var check=new NpgsqlCommand("""
SELECT EXISTS(SELECT 1 FROM employment_tracker_events
 WHERE url=@u AND event_type='Applied')
""",db,tx);
   check.Parameters.AddWithValue("u",url);
   if(await check.ExecuteScalarAsync(ct) is not true)
    throw new InvalidOperationException("Record Applied before application outcome events");
  }
  await using(var insert=new NpgsqlCommand("""
INSERT INTO employment_tracker_events(url,event_type,own_note)
VALUES(@u,@e,@n)
ON CONFLICT DO NOTHING RETURNING id
""",db,tx))
  {
   insert.Parameters.AddWithValue("u",url);
   insert.Parameters.AddWithValue("e",eventType);
   insert.Parameters.AddWithValue("n",NpgsqlDbType.Text,(object?)ownNote??DBNull.Value);
   if(await insert.ExecuteScalarAsync(ct) is null)
    throw new InvalidOperationException("Applied already recorded; duplicate blocked");
  }
  await using(var update=new NpgsqlCommand(
    "UPDATE employment_tracker SET updated_at=now() WHERE url=@u",db,tx))
  {
   update.Parameters.AddWithValue("u",url);
   await update.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
 }
 public async Task<IReadOnlyList<TrackedEmployment>> ReadTrackedEmploymentAsync(CancellationToken ct)
 {
  const string sql="""
SELECT t.url,t.label,t.created_at,t.updated_at,
       COALESCE((SELECT e.event_type FROM employment_tracker_events e
          WHERE e.url=t.url ORDER BY e.id DESC LIMIT 1),'Saved'),
       (SELECT min(e.event_at) FROM employment_tracker_events e
          WHERE e.url=t.url AND e.event_type='Applied'),
       (SELECT count(*) FROM employment_tracker_events e WHERE e.url=t.url)
FROM employment_tracker t
ORDER BY t.updated_at DESC,t.url
""";
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,db);
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  var rows=new List<TrackedEmployment>();
  while(await reader.ReadAsync(ct))
  {
   DateTimeOffset timestamp(int i)=>
    new(DateTime.SpecifyKind(reader.GetDateTime(i),DateTimeKind.Utc));
   rows.Add(new TrackedEmployment(reader.GetString(0),
    reader.IsDBNull(1)?null:reader.GetString(1),
    timestamp(2),timestamp(3),reader.GetString(4),
    reader.IsDBNull(5)?null:timestamp(5),reader.GetInt64(6)));
  }
  return rows;
 }
 public async Task<bool> DeleteTrackedEmploymentAsync(string url,CancellationToken ct)
 {
  url=EmploymentTrackingPolicy.Url(url);
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand("DELETE FROM employment_tracker WHERE url=@u",db);
  cmd.Parameters.AddWithValue("u",url);
  return await cmd.ExecuteNonQueryAsync(ct)==1;
 }
}
