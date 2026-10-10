using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>
/// An opt-in, transient search of authenticated Freelancer.com projects.
/// Project opportunities are not employment vacancies: no writes to jobs or
/// PostgreSQL, and no unattended execution. Do not run without documented
/// provider permission for automated access.
/// </summary>
public static class FreelancerProjects
{
 public const string ApiBase="https://www.freelancer.com/api/projects/0.1/projects/active/";
 private const string SiteBase="https://www.freelancer.com/projects/";
 private static readonly Regex Net=new(
  @"(?<!\w)(?:c#|csharp|c[- ]sharp|\.net|dotnet|asp\.net)(?!\w)",
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 private static readonly Regex Integration=new(
  @"\b(?:api|rest|webhooks?|integrat(?:e|ion|ions)|backend|back-end|postgre(?:s|sql)|sql|rabbitmq)\b",
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 private static readonly Regex Trading=new(
  @"\b(?:trad(?:e|ing|er)|broker|exchange|deribit|bitmex|crypto|fintech|bot|execution)\b",
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 private static readonly Regex Ai=new(
  @"\b(?:python|onnx|ai|llm|automation|microservices?)\b",
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 private static readonly Regex Unrelated=new(
  @"\b(?:logo|graphic design|illustrator|wordpress theme|data entry|seo writing)\b",
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);

 public static void RequireAuthorized(string? permission, string? token)
 {
  if(!string.Equals(permission,"true",StringComparison.OrdinalIgnoreCase))
   throw new InvalidOperationException(
    "Freelancer automation is disabled. Obtain express written permission from Freelancer.com and set FLN_AUTOMATION_PERMISSION_GRANTED=true.");
  if(string.IsNullOrWhiteSpace(token))
   throw new InvalidOperationException(
    "Freelancer OAuth token is missing. Set FLN_OAUTH_TOKEN only in the local process environment.");
 }

 public static Uri SearchUri(string query,int limit,int offset)
 {
  if(string.IsNullOrWhiteSpace(query))throw new ArgumentException("Empty query",nameof(query));
  if(limit is <1 or >100 || offset<0)throw new ArgumentOutOfRangeException(nameof(limit));
  return new Uri(ApiBase+"?query="+Uri.EscapeDataString(query)+
    "&limit="+limit.ToString(CultureInfo.InvariantCulture)+
    "&offset="+offset.ToString(CultureInfo.InvariantCulture));
 }

 public static IReadOnlyList<FreelancerProject> ParseSearch(string json)
 {
  using var doc=JsonDocument.Parse(json);
  if(doc.RootElement.TryGetProperty("status",out var status) &&
     status.ValueKind==JsonValueKind.String &&
     !string.Equals(status.GetString(),"success",StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("Freelancer API reported an unsuccessful response");
  if(!doc.RootElement.TryGetProperty("result",out var result)||
     result.ValueKind!=JsonValueKind.Object||
     !result.TryGetProperty("projects",out var items)||
     items.ValueKind!=JsonValueKind.Array)
   throw new InvalidDataException("Freelancer API search response missing result.projects");
  var parsed=new List<FreelancerProject>();
  foreach(var item in items.EnumerateArray())
  {
   if(item.ValueKind!=JsonValueKind.Object)continue;
   var id=Long(item,"id");
   var title=Str(item,"title");
   if(!id.HasValue || id.Value<=0 || string.IsNullOrWhiteSpace(title))continue;
   string? seo=Str(item,"seo_url");
   var path=string.IsNullOrWhiteSpace(seo)?id.Value.ToString(CultureInfo.InvariantCulture):seo.Trim('/');
   // Never accept arbitrary external URL or path traversal from provider fields.
   if(!Regex.IsMatch(path,@"\A[a-zA-Z0-9_-]+\z",RegexOptions.CultureInvariant))
    path=id.Value.ToString(CultureInfo.InvariantCulture);
   var description=Str(item,"preview_description")??
      Str(item,"description")??"";
   description=Regex.Replace(System.Net.WebUtility.HtmlDecode(description),
     @"\s+"," ").Trim();
   if(description.Length>450)description=description[..450]+"…";
   JsonElement budget=default,currency=default;
   if(item.TryGetProperty("budget",out var b)&&b.ValueKind==JsonValueKind.Object)budget=b;
   if(item.TryGetProperty("currency",out var c)&&c.ValueKind==JsonValueKind.Object)currency=c;
   var currencyCode=Str(currency,"code");
   decimal? min=Money(budget,"minimum"),max=Money(budget,"maximum");
   var type=Str(item,"type");
   var skills=new List<string>();
   if(item.TryGetProperty("jobs",out var jobs)&&jobs.ValueKind==JsonValueKind.Array)
    foreach(var skill in jobs.EnumerateArray())
    {
     string? name=Str(skill,"name");
     if(!string.IsNullOrWhiteSpace(name)&&skills.Count<25)skills.Add(name);
    }
   parsed.Add(new FreelancerProject(id.Value,SiteBase+path,title.Trim(),
    description,currencyCode,min,max,type,skills));
  }
  return parsed;
 }

 private static string? Str(JsonElement json,string name) =>
  json.ValueKind==JsonValueKind.Object&&json.TryGetProperty(name,out var v)&&
  v.ValueKind==JsonValueKind.String ? v.GetString() : null;

 private static long? Long(JsonElement json,string name)
 {
  if(json.ValueKind!=JsonValueKind.Object||!json.TryGetProperty(name,out var v))return null;
  return long.TryParse(v.ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out var id)?id:null;
 }

 private static decimal? Money(JsonElement json,string name)
 {
  if(json.ValueKind!=JsonValueKind.Object||!json.TryGetProperty(name,out var v))return null;
  return decimal.TryParse(v.ToString(),NumberStyles.Number,CultureInfo.InvariantCulture,out var d)?d:null;
 }

 public static FreelancerCandidate Rank(FreelancerProject job,IReadOnlyList<string> queries)
 {
  var text=job.Title+" "+job.Summary+" "+string.Join(" ",job.Skills);
  int score=0;
  var reasons=new List<string>();
  if(Net.IsMatch(text)){score+=40;reasons.Add("C#/.NET");}
  if(Integration.IsMatch(text)){score+=20;reasons.Add("API/backend/integration");}
  if(Trading.IsMatch(text)){score+=20;reasons.Add("Trading/broker/exchange");}
  if(Ai.IsMatch(text)){score+=10;reasons.Add("Automation/AI/Python");}
  if(Unrelated.IsMatch(text)){score-=25;reasons.Add("Possible unrelated work");}
  var bucket=score>=55?"Priority":score>=20?"Review":"LowMatch";
  return new FreelancerCandidate(job,queries,bucket,score,reasons);
 }

 public static async Task<FreelancerScanSummary> ScanAsync(
  HttpClient client,TextWriter output,string? token,string? permission,
  IEnumerable<string> configuredQueries,int pageSize,int maxPages,
  Action<string>? progress,CancellationToken ct)
 {
  // Must run before any outgoing request or parsing of results.
  RequireAuthorized(permission,token);
  var queries=configuredQueries.Where(x=>!string.IsNullOrWhiteSpace(x))
   .Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
  if(queries.Length==0)throw new InvalidOperationException("No Freelancer queries configured");
  int limit=Math.Clamp(pageSize,1,50),pages=Math.Clamp(maxPages,1,2);
  var found=new Dictionary<long,(FreelancerProject Project,HashSet<string> Queries)>();
  int calls=0;
  foreach(var query in queries)
  {
   for(int page=0;page<pages;page++)
   {
    ct.ThrowIfCancellationRequested();
    // Bounded manual scan; not a background crawler. Never hammer on failures.
    if(calls>0)await Task.Delay(TimeSpan.FromMilliseconds(1200),ct);
    using var request=new HttpRequestMessage(HttpMethod.Get,SearchUri(query,limit,page*limit));
    request.Headers.TryAddWithoutValidation("Freelancer-OAuth-V1",token);
    using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
    calls++;
    if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
     throw new InvalidDataException("Freelancer API denied authenticated access (HTTP "+
      (int)response.StatusCode+"); stop and review permission/token scopes");
    if((int)response.StatusCode==429)
     throw new InvalidDataException("Freelancer API rate limit (HTTP 429); scan stopped without retry");
    response.EnsureSuccessStatusCode();
    string json=await response.Content.ReadAsStringAsync(ct);
    var records=ParseSearch(json);
    foreach(var item in records)
    {
     if(found.TryGetValue(item.Id,out var prior))
      prior.Queries.Add(query);
     else found.Add(item.Id,(item,new HashSet<string>(StringComparer.OrdinalIgnoreCase){query}));
    }
    progress?.Invoke($"Freelancer query {query}: page {page+1}/{pages}, received={records.Count}, unique={found.Count}");
    if(records.Count<limit)break; // partial page; no claim of site-wide completeness
   }
  }
  var results=found.Values.Select(x=>Rank(x.Project,x.Queries.Order(StringComparer.OrdinalIgnoreCase).ToArray()))
   .OrderByDescending(x=>x.Score).ThenByDescending(x=>x.Project.Id).ToArray();
  foreach(var item in results)
  {
   ct.ThrowIfCancellationRequested();
   await output.WriteLineAsync(JsonlOutput.Serialize(item).AsMemory(),ct);
  }
  await output.FlushAsync(ct);
  return new FreelancerScanSummary(queries.Length,calls,results.Length,
   results.Count(x=>x.Bucket=="Priority"),results.Count(x=>x.Bucket=="Review"),
   results.Count(x=>x.Bucket=="LowMatch"));
 }
}

public sealed record FreelancerProject(
 long Id,string Url,string Title,string Summary,string? Currency,
 decimal? BudgetMinimum,decimal? BudgetMaximum,string? Type,IReadOnlyList<string> Skills);
public sealed record FreelancerCandidate(
 FreelancerProject Project,IReadOnlyList<string> Queries,string Bucket,int Score,IReadOnlyList<string> Reasons);
public sealed record FreelancerScanSummary(
 int Queries,int ApiCalls,int UniqueProjects,int Priority,int Review,int LowMatch);
