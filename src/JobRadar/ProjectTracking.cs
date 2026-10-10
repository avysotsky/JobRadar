using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace JobRadar;

/// <summary>
/// Strictly user-entered bookmarks and own application actions, NOT source API
/// data. No provider request, scraping, proposal submission or credential use.
/// </summary>
public static class ProjectTrackerPolicy
{
 public static string Provider(string raw)
 {
  var value=(raw??"").Trim().ToLowerInvariant();
  if(value is not ("freelancehunt" or "freelancer"))
   throw new ArgumentException("Supported project provider: freelancehunt or freelancer");
  return value;
 }

 public static string Url(string provider,string raw)
 {
  provider=Provider(provider);
  if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri)||
    uri.Scheme!=Uri.UriSchemeHttps || !uri.IsDefaultPort ||
    !string.IsNullOrEmpty(uri.UserInfo))
   throw new ArgumentException("Project URL must be an HTTPS provider URL without credentials or a custom port");
  string host=provider=="freelancehunt"?"freelancehunt.com":"freelancer.com";
  string prefix=provider=="freelancehunt"?"/project/":"/projects/";
  if(!(uri.Host.Equals(host,StringComparison.OrdinalIgnoreCase)||
       uri.Host.Equals("www."+host,StringComparison.OrdinalIgnoreCase))||
     !uri.AbsolutePath.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||
     uri.AbsolutePath.Length<=prefix.Length)
   throw new ArgumentException("Project URL does not belong to the selected provider");
  // Provider tracking/query parameters may contain tokens. Never retain them.
  var canonical=new UriBuilder(uri){Query="",Fragment="",Host=host}.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
  if(canonical.Length>1500)throw new ArgumentException("Project URL too long");
  return canonical;
 }

 public static string? Label(string? value)
 {
  if(value is null)return null;
  var label=value.Trim();
  if(label.Length>180 || label.Any(char.IsControl))
   throw new ArgumentException("Manual label must be <=180 printable characters");
  return label.Length==0?null:label;
 }

 public static string? Note(string? value)
 {
  if(value is null)return null;
  var note=value.Trim();
  if(note.Length>500 || note.Any(c=>c=='\0'))
   throw new ArgumentException("Own note must be <=500 characters");
  return note.Length==0?null:note;
 }

 public static string? Currency(string? raw)
 {
  if(raw is null)return null;
  var value=raw.Trim().ToUpperInvariant();
  if(value is not ("USD" or "EUR" or "UAH"))
   throw new ArgumentException("Manual budget currency must be USD, EUR or UAH");
  return value;
 }

 public static decimal? Budget(decimal? amount)
 {
  if(amount.HasValue&&(amount.Value<0||amount.Value>999999999m))
   throw new ArgumentOutOfRangeException(nameof(amount));
  return amount;
 }

 public static string Event(string? raw)
 {
  var value=(raw??"").Trim();
  if(value is not ("Applied" or "Replied" or "Interview" or "Won" or
                   "Rejected" or "Withdrawn" or "Closed"))
   throw new ArgumentException("Event must be Applied, Replied, Interview, Won, Rejected, Withdrawn or Closed");
  return value;
 }
}

public sealed record TrackedProject(
 string Provider,string Url,string? Label,decimal? OwnBudget,string? OwnCurrency,
 DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,string CurrentStatus,
 DateTimeOffset? AppliedAt,long EventCount);

public sealed partial class Storage
{
 public async Task SaveManualProjectAsync(
  string provider,string url,string? label,decimal? budget,string? currency,CancellationToken ct)
 {
  provider=ProjectTrackerPolicy.Provider(provider);
  url=ProjectTrackerPolicy.Url(provider,url);
  label=ProjectTrackerPolicy.Label(label);
  budget=ProjectTrackerPolicy.Budget(budget);
  currency=ProjectTrackerPolicy.Currency(currency);
  if(budget.HasValue!= (currency is not null))
   throw new ArgumentException("Manual budget and currency must be supplied together");
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand("""
INSERT INTO project_tracker(provider,url,label,own_budget,own_currency)
VALUES(@p,@u,@l,@b,@c)
ON CONFLICT(provider,url) DO UPDATE SET
 label=COALESCE(EXCLUDED.label,project_tracker.label),
 own_budget=COALESCE(EXCLUDED.own_budget,project_tracker.own_budget),
 own_currency=COALESCE(EXCLUDED.own_currency,project_tracker.own_currency),
 updated_at=now()
""",db);
  cmd.Parameters.AddWithValue("p",provider);
  cmd.Parameters.AddWithValue("u",url);
  cmd.Parameters.AddWithValue("l",NpgsqlDbType.Text,(object?)label??DBNull.Value);
  cmd.Parameters.AddWithValue("b",NpgsqlDbType.Numeric,(object?)budget??DBNull.Value);
  cmd.Parameters.AddWithValue("c",NpgsqlDbType.Text,(object?)currency??DBNull.Value);
  await cmd.ExecuteNonQueryAsync(ct);
 }

 public async Task RecordOwnProjectEventAsync(
  string provider,string url,string eventType,string? ownNote,CancellationToken ct)
 {
  provider=ProjectTrackerPolicy.Provider(provider);
  url=ProjectTrackerPolicy.Url(provider,url);
  eventType=ProjectTrackerPolicy.Event(eventType);
  ownNote=ProjectTrackerPolicy.Note(ownNote);

  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  // Serialize concurrent local events for the same project.
  await using(var lockCmd=new NpgsqlCommand("""
SELECT 1 FROM project_tracker WHERE provider=@p AND url=@u FOR UPDATE
""",db,tx))
  {
   lockCmd.Parameters.AddWithValue("p",provider);
   lockCmd.Parameters.AddWithValue("u",url);
   if(await lockCmd.ExecuteScalarAsync(ct) is null)
    throw new InvalidOperationException("Save the project reference before recording an event");
  }
  if(eventType is "Replied" or "Interview" or "Won" or "Rejected" or "Withdrawn")
  {
   await using var check=new NpgsqlCommand("""
SELECT EXISTS(SELECT 1 FROM project_tracker_events
 WHERE provider=@p AND url=@u AND event_type='Applied')
""",db,tx);
   check.Parameters.AddWithValue("p",provider);
   check.Parameters.AddWithValue("u",url);
   if(await check.ExecuteScalarAsync(ct) is not true)
    throw new InvalidOperationException("Record Applied before application-outcome events");
  }
  await using(var insert=new NpgsqlCommand("""
INSERT INTO project_tracker_events(provider,url,event_type,own_note)
VALUES(@p,@u,@e,@n)
ON CONFLICT DO NOTHING RETURNING id
""",db,tx))
  {
   insert.Parameters.AddWithValue("p",provider);
   insert.Parameters.AddWithValue("u",url);
   insert.Parameters.AddWithValue("e",eventType);
   insert.Parameters.AddWithValue("n",NpgsqlDbType.Text,(object?)ownNote??DBNull.Value);
   if(await insert.ExecuteScalarAsync(ct) is null)
    throw new InvalidOperationException("Applied already recorded: duplicate submissions are blocked");
  }
  await using(var update=new NpgsqlCommand("""
UPDATE project_tracker SET updated_at=now()
WHERE provider=@p AND url=@u
""",db,tx))
  {
   update.Parameters.AddWithValue("p",provider);
   update.Parameters.AddWithValue("u",url);
   await update.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
 }

 public async Task<IReadOnlyList<TrackedProject>> ReadTrackedProjectsAsync(CancellationToken ct)
 {
  const string sql="""
SELECT p.provider,p.url,p.label,p.own_budget,p.own_currency,
       p.created_at,p.updated_at,
       COALESCE((SELECT e.event_type FROM project_tracker_events e
         WHERE e.provider=p.provider AND e.url=p.url
         ORDER BY e.event_at DESC,e.id DESC LIMIT 1),'Saved'),
       (SELECT min(e.event_at) FROM project_tracker_events e
        WHERE e.provider=p.provider AND e.url=p.url AND e.event_type='Applied'),
       (SELECT count(*) FROM project_tracker_events e
        WHERE e.provider=p.provider AND e.url=p.url)
FROM project_tracker p
ORDER BY p.updated_at DESC,p.provider,p.url
""";
  var result=new List<TrackedProject>();
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(sql,db);
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   DateTimeOffset timestamp(int index)=>
    new(DateTime.SpecifyKind(reader.GetDateTime(index),DateTimeKind.Utc));
   result.Add(new TrackedProject(
    reader.GetString(0),reader.GetString(1),
    reader.IsDBNull(2)?null:reader.GetString(2),
    reader.IsDBNull(3)?null:reader.GetDecimal(3),
    reader.IsDBNull(4)?null:reader.GetString(4),
    timestamp(5),timestamp(6),reader.GetString(7),
    reader.IsDBNull(8)?null:timestamp(8),reader.GetInt64(9)));
  }
  return result;
 }

 public async Task<bool> DeleteTrackedProjectAsync(
  string provider,string url,CancellationToken ct)
 {
  provider=ProjectTrackerPolicy.Provider(provider);
  url=ProjectTrackerPolicy.Url(provider,url);
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand(
   "DELETE FROM project_tracker WHERE provider=@p AND url=@u",db);
  cmd.Parameters.AddWithValue("p",provider);
  cmd.Parameters.AddWithValue("u",url);
  // Own event history is deleted by ON DELETE CASCADE.
  return await cmd.ExecuteNonQueryAsync(ct)==1;
 }
}
