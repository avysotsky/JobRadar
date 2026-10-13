using Npgsql;
namespace JobRadar;

/// <summary>Explicit user-entered status, not provider status evidence.</summary>
public sealed record EmploymentDecision(string Url,string Decision,DateTimeOffset UpdatedAt);

public sealed partial class Storage
{
 /// <summary>
 /// Mark a vacancy URL Applied or Inactive from the owner's own knowledge.
 /// No provider HTTP calls or automatic inferred closures are involved.
 /// </summary>
 public async Task MarkEmploymentDecisionAsync(string url,string decision,CancellationToken ct)
 {
  if(!IsAcceptableEmploymentUrl(url))throw new ArgumentException(
   "Employment URL must be absolute HTTPS without credentials or fragment",nameof(url));
  if(decision is not ("Applied" or "Inactive"))
   throw new ArgumentException("Allowed decisions: Applied, Inactive",nameof(decision));
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var command=new NpgsqlCommand("""
INSERT INTO employment_decisions(url,decision,updated_at)
VALUES(@url,@decision,now())
ON CONFLICT(url) DO UPDATE SET decision=excluded.decision,updated_at=now()
""",db);
  command.Parameters.AddWithValue("url",CanonicalEmploymentUrl(url));
  command.Parameters.AddWithValue("decision",decision);
  await command.ExecuteNonQueryAsync(ct);
 }

 public async Task<IReadOnlyDictionary<string,EmploymentDecision>> ReadEmploymentDecisionsAsync(
  CancellationToken ct)
 {
  await using var db=new NpgsqlConnection(connectionString);
  await db.OpenAsync(ct);
  await using var command=new NpgsqlCommand(
   "SELECT url,decision,updated_at FROM employment_decisions ORDER BY url",db);
  await using var reader=await command.ExecuteReaderAsync(ct);
  var result=new Dictionary<string,EmploymentDecision>(StringComparer.OrdinalIgnoreCase);
  while(await reader.ReadAsync(ct))
  {
   var url=reader.GetString(0);
   var timestamp=new DateTimeOffset(DateTime.SpecifyKind(
    reader.GetDateTime(2),DateTimeKind.Utc));
   result[url]=new EmploymentDecision(url,reader.GetString(1),timestamp);
  }
  return result;
 }

 public static bool IsAcceptableEmploymentUrl(string? url)=>
  url is {Length:>0 and <=1500} &&
  Uri.TryCreate(url,UriKind.Absolute,out var uri) &&
  uri.Scheme==Uri.UriSchemeHttps && uri.UserInfo.Length==0 &&
  uri.Fragment.Length==0 && uri.Host.Contains('.') &&
  uri.AbsolutePath.Length>1;

 public static string CanonicalEmploymentUrl(string url)
 {
  if(!IsAcceptableEmploymentUrl(url))throw new ArgumentException("Invalid employment URL",nameof(url));
  var uri=new Uri(url);
  // Preserve significant query parameters but ignore trailing "/" differences.
  // Only explicitly tagged exact URL identities are affected; matching similar
  // titles across different providers is intentionally NOT automatic.
  var builder=new UriBuilder(uri){Fragment="",Host=uri.Host.ToLowerInvariant()};
  var result=builder.Uri.AbsoluteUri;
  var queryAt=result.IndexOf('?');
  var prefix=queryAt<0?result:result[..queryAt];
  var suffix=queryAt<0?"":result[queryAt..];
  return prefix.TrimEnd('/')+suffix;
 }
}
