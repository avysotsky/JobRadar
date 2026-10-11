using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>
/// User-authorized, time-limited, LOCAL snapshot from the connected Upwork
/// application. Upwork retired RSS and a connected ChatGPT app does not grant
/// this Windows executable a reusable API credential.
/// No HTTP, authentication, database writes, applications or Connects spend.
/// </summary>
public static class UpworkProjects
{
 private static readonly Regex IdPath=new(
  @"^/jobs/~0[12][0-9]{12,22}/?$",
  RegexOptions.Compiled|RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);

 public const int MaxSnapshotBytes=2_000_000;
 public const int MaxRows=100;
 public static readonly TimeSpan MaxAge=TimeSpan.FromHours(24);

 public static string? CanonicalUrl(string? raw)
 {
  if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri) ||
     uri.Scheme!=Uri.UriSchemeHttps || !uri.IsDefaultPort ||
     !string.IsNullOrEmpty(uri.UserInfo) ||
     !(uri.Host.Equals("www.upwork.com",StringComparison.OrdinalIgnoreCase)||
       uri.Host.Equals("upwork.com",StringComparison.OrdinalIgnoreCase))||
     !IdPath.IsMatch(uri.AbsolutePath))return null;
  var builder=new UriBuilder(uri){Host="www.upwork.com",Query="",Fragment=""};
  return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
 }

 public static bool TryAssess(string json,DateTimeOffset now,out UpworkCandidate? result)
 {
  result=null;
  try
  {
   var item=JsonSerializer.Deserialize<UpworkSnapshot>(json,JsonlOutput.Options);
   var url=CanonicalUrl(item?.Url);
   if(item is null||url is null||
      string.IsNullOrWhiteSpace(item.Title)||item.Title.Length>240 ||
      item.Title.Any(char.IsControl)||item.Skills is null||item.Skills.Count>30||
      item.Skills.Any(x=>x is null||x.Length>100)||
      item.CapturedAt==default||now-item.CapturedAt>MaxAge||
      item.CapturedAt>now+TimeSpan.FromMinutes(5)||
      (item.DescriptionSnippet?.Length??0)>12000 ||
      (item.Budget?.Length??0)>100 ||
      (item.ProposalsTier?.Length??0)>100 ||
      (item.ExperienceLevel?.Length??0)>40 ||
      (item.JobType?.Length??0)>40)return false;
   var description=Clean(item.DescriptionSnippet??"");
   var budget=Clean(item.Budget??"");
   var scoreProject=new FreelancerProject(0,url,item.Title,description,
    null,null,null,item.JobType,item.Skills);
   var match=FreelancerProjects.Rank(scoreProject,["upwork"]);
   var bucket=match.Bucket;
   var reasons=match.Reasons.ToList();
   var warnings=new List<string>
   {
    "Upwork connector snapshot: project open status and application eligibility are unverified",
    "Connects cost and client preferred qualifications require checking on Upwork",
    "Client country is not the contractor's work-location eligibility"
   };
   if(item.ExperienceLevel?.Equals("expert",StringComparison.OrdinalIgnoreCase)==true)
   {
    warnings.Add("Upwork labels this project Expert: confirm role expectations");
    if(bucket=="Priority")bucket="Review";
   }
   if(Regex.IsMatch(item.Title,@"\b(?:senior|principal|lead|architect)\b",
      RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
   {
    warnings.Add("Senior/lead wording in title: review before bidding");
    if(bucket=="Priority")bucket="Review";
   }
   if(item.Applied==true)
   {
    warnings.Add("Already applied on Upwork: do not submit another proposal");
    if(bucket=="Priority")bucket="Review";
   }
   if(item.PublishedAt is { } published && published>now+TimeSpan.FromDays(1))
    warnings.Add("Provider publication date is in the future: verify");
   result=new UpworkCandidate("upwork",url,item.Title,budget.Length==0?null:budget,
    item.JobType,item.ExperienceLevel,item.PublishedAt,item.CapturedAt,
    item.ProposalsTier,item.ClientCountry,item.Applied,
    bucket,match.Score,reasons,warnings);
   return true;
  }
  catch(JsonException) {return false;}
  catch(ArgumentException) {return false;}
 }

 private static string Clean(string value)=>
  Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value,
   @"</?[^>]+>", " ")),@"\s+"," ").Trim();

 public static async Task<UpworkSnapshotReport> ReviewFileAsync(
  string path,TextWriter output,CancellationToken ct)
 {
  var info=new FileInfo(path);
  if(!info.Exists)throw new FileNotFoundException("Upwork local snapshot not found");
  if(info.Length>MaxSnapshotBytes)
   throw new InvalidDataException("Upwork snapshot exceeds 2 MiB limit");
  var now=DateTimeOffset.UtcNow;
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  int read=0,accepted=0,rejected=0,duplicate=0,priority=0,review=0,low=0;
  using var reader=new StreamReader(path,new UTF8Encoding(false,true),true);
  while(await reader.ReadLineAsync(ct) is { } line)
  {
   if(string.IsNullOrWhiteSpace(line))continue;
   if(++read>MaxRows)throw new InvalidDataException("Upwork snapshot has more than 100 rows");
   if(line.Length>32_000||!TryAssess(line,now,out var assessed))
   {
    rejected++;continue;
   }
   if(!seen.Add(assessed!.Url)){duplicate++;continue;}
   accepted++;
   switch(assessed.Bucket)
   {
    case "Priority":priority++;break;
    case "Review":review++;break;
    default:low++;break;
   }
   await output.WriteLineAsync(JsonlOutput.Serialize(assessed).AsMemory(),ct);
  }
  await output.FlushAsync(ct);
  return new UpworkSnapshotReport(read,accepted,rejected,duplicate,
   priority,review,low,"TRANSIENT_CONNECTOR_SNAPSHOT_NOT_PROVIDER_LIVE",
   "No network, no DB writes, no bids. Delete raw provider snapshot within 24 hours; status and Connects unverified.");
 }
}

public sealed class UpworkSnapshot
{
 [JsonPropertyName("url")]public string? Url {get;set;}
 [JsonPropertyName("title")]public string? Title {get;set;}
 [JsonPropertyName("description_snippet")]public string? DescriptionSnippet {get;set;}
 [JsonPropertyName("skills")]public IReadOnlyList<string> Skills {get;set;}=[];
 [JsonPropertyName("budget")]public string? Budget {get;set;}
 [JsonPropertyName("job_type")]public string? JobType {get;set;}
 [JsonPropertyName("experience_level")]public string? ExperienceLevel {get;set;}
 [JsonPropertyName("published_date")]public DateTimeOffset? PublishedAt {get;set;}
 [JsonPropertyName("captured_at")]public DateTimeOffset CapturedAt {get;set;}
 [JsonPropertyName("proposals_tier")]public string? ProposalsTier {get;set;}
 [JsonPropertyName("client_country")]public string? ClientCountry {get;set;}
 [JsonPropertyName("applied")]public bool? Applied {get;set;}
}

public sealed record UpworkCandidate(
 string Source,string Url,string Title,string? BudgetLabel,
 string? JobType,string? ExperienceLevel,DateTimeOffset? PublishedAt,
 DateTimeOffset CapturedAt,string? ProposalsTier,string? ClientCountry,
 bool? Applied,string Bucket,int Score,IReadOnlyList<string> Reasons,
 IReadOnlyList<string> Warnings);

public sealed record UpworkSnapshotReport(
 int LinesRead,int Accepted,int Rejected,int Duplicates,
 int Priority,int Review,int LowMatch,string CoverageStatus,string Notice);
