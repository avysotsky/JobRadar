using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>
/// Read-only, manual Freelancehunt API v2 project discovery. Freelance projects
/// never enter the employment-vacancy database. No bid/POST endpoint is used.
/// </summary>
public static class FreelancehuntProjects
{
 public const string ApiBase="https://api.freelancehunt.com/v2/projects";

 public static Uri PageUri(int page,IReadOnlyCollection<int> skillIds)
 {
  if(page is <1 or >100)throw new ArgumentOutOfRangeException(nameof(page));
  if(skillIds.Count>12 || skillIds.Any(id=>id<=0))
   throw new ArgumentException("1-12 valid positive skill IDs are allowed",nameof(skillIds));
  var uri=ApiBase+"?page%5Bnumber%5D="+page.ToString(CultureInfo.InvariantCulture);
  if(skillIds.Count>0)
   uri+="&filter%5Bskill_id%5D="+string.Join("%2C",skillIds.Distinct().Order());
  return new Uri(uri);
 }

 public static FreelancehuntPage ParsePage(string json)
 {
  using var doc=JsonDocument.Parse(json);
  var root=doc.RootElement;
  if(root.ValueKind!=JsonValueKind.Object ||
     !root.TryGetProperty("data",out var data) ||
     data.ValueKind!=JsonValueKind.Array)
   throw new InvalidDataException("Freelancehunt API response missing data array");
  var projects=new List<FreelancehuntProject>();
  int raw=0,dropped=0;
  foreach(var item in data.EnumerateArray())
  {
   raw++;
   if(!TryProject(item,out var project)){dropped++;continue;}
   projects.Add(project!);
  }
  bool nextKnown=false,hasNext=false;
  if(root.TryGetProperty("links",out var links)&&links.ValueKind==JsonValueKind.Object &&
     links.TryGetProperty("next",out var next))
  {
   nextKnown=true;
   hasNext=next.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(next.GetString());
   if(next.ValueKind!=JsonValueKind.Null && next.ValueKind!=JsonValueKind.String)
    throw new InvalidDataException("Freelancehunt API links.next has unexpected type");
  }
  return new FreelancehuntPage(projects,raw,dropped,hasNext,nextKnown);
 }

 private static bool TryProject(JsonElement entry,out FreelancehuntProject? project)
 {
  project=null;
  if(entry.ValueKind!=JsonValueKind.Object ||
    !entry.TryGetProperty("attributes",out var attrs) ||
    attrs.ValueKind!=JsonValueKind.Object)return false;
  if(!long.TryParse(Value(entry,"id"),NumberStyles.None,
      CultureInfo.InvariantCulture,out long id)||id<=0)return false;
  var title=Value(attrs,"name");
  if(string.IsNullOrWhiteSpace(title))return false;
  // Use only the actual web URL furnished by the API, never invent a slug.
  if(!entry.TryGetProperty("links",out var links) ||
     !TryWebUrl(links,out var web))return false;
  var description=Value(attrs,"description");
  if(string.IsNullOrWhiteSpace(description))
   description=Value(attrs,"description_html")??"";
  description=Regex.Replace(description,@"<[^>]*>"," ",RegexOptions.Singleline);
  description=Regex.Replace(WebUtility.HtmlDecode(description),@"\s+"," ").Trim();
  if(description.Length>1400)description=description[..1400]+"…";

  var skills=new List<string>();
  if(attrs.TryGetProperty("skills",out var skillArray)&&skillArray.ValueKind==JsonValueKind.Array)
   foreach(var skill in skillArray.EnumerateArray())
   {
    var name=Value(skill,"name");
    if(!string.IsNullOrWhiteSpace(name)&&skills.Count<30)skills.Add(name);
   }
  JsonElement budget=default;
  if(attrs.TryGetProperty("budget",out var b)&&b.ValueKind==JsonValueKind.Object)budget=b;
  decimal? amount=null;
  if(decimal.TryParse(Value(budget,"amount"),NumberStyles.Number,
    CultureInfo.InvariantCulture,out var parsedAmount))amount=parsedAmount;
  DateTimeOffset? published=null;
  if(DateTimeOffset.TryParse(Value(attrs,"published_at"),CultureInfo.InvariantCulture,
    DateTimeStyles.None,out var parsedDate))published=parsedDate;
  int? bids=null;
  if(int.TryParse(Value(attrs,"bid_count"),NumberStyles.None,
      CultureInfo.InvariantCulture,out int bidCount)&&bidCount>=0)bids=bidCount;
  var status=attrs.TryGetProperty("status",out var st)?Value(st,"name"):null;
  project=new FreelancehuntProject(id,web!,title.Trim(),description,
   amount,Value(budget,"currency"),skills,bids,
   Bool(attrs,"is_only_for_plus"),Bool(attrs,"is_remote_job"),
   published,status);
  return true;
 }

 private static bool TryWebUrl(JsonElement links,out string? web)
 {
  web=null;
  if(links.ValueKind!=JsonValueKind.Object ||
     !links.TryGetProperty("self",out var self))return false;
  var candidate=Value(self,"web");
  if(!Uri.TryCreate(candidate,UriKind.Absolute,out var url) ||
     url.Scheme!=Uri.UriSchemeHttps ||
     url.Host is not ("freelancehunt.com" or "www.freelancehunt.com") ||
     !url.AbsolutePath.StartsWith("/project/",StringComparison.OrdinalIgnoreCase))
    return false;
  web=url.GetLeftPart(UriPartial.Path);
  return true;
 }

 private static bool? Bool(JsonElement obj,string name)
 {
  if(obj.ValueKind!=JsonValueKind.Object || !obj.TryGetProperty(name,out var prop))return null;
  return prop.ValueKind switch
  {
   JsonValueKind.True => true, JsonValueKind.False => false,_=>null
  };
 }

 private static string? Value(JsonElement obj,string name)
 {
  if(obj.ValueKind!=JsonValueKind.Object ||
     !obj.TryGetProperty(name,out var value))return null;
  return value.ValueKind switch
  {
   JsonValueKind.String => value.GetString(),
   JsonValueKind.Number => value.ToString(),
   _=>null
  };
 }

 public static FreelancehuntCandidate Rank(FreelancehuntProject project)
 {
  // Reuse the project-oriented scorer; never apply employment seniority,
  // geolocation or English rules to freelance projects.
  var proxy=new FreelancerProject(project.Id,project.Url,project.Title,
   project.Description,project.Currency,project.Budget,project.Budget,
   "Freelancehunt",project.Skills);
  var match=FreelancerProjects.Rank(proxy,["Freelancehunt"]);
  var reasons=match.Reasons.ToList();
  var bucket=match.Bucket;
  if(project.PlusOnly==true)
  {
   reasons.Add("Plus account requirement — verify eligibility before bidding");
   if(bucket=="Priority")bucket="Review";
  }
  // Freelancehunt 'is_remote_job' differentiates a remote job posting from
  // an ordinary freelance project; false does NOT mean office attendance.
  // Preserve the provider flag without using it as a negative eligibility gate.
  return new FreelancehuntCandidate(project,bucket,match.Score,reasons);
 }

 public static async Task<FreelancehuntReport> ScanAsync(
  HttpClient client,TextWriter output,string? token,
  IReadOnlyCollection<int> skillIds,int requestedPages,
  Action<string>? progress,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(token))
   throw new InvalidOperationException(
    "Freelancehunt API token missing; set FREELANCEHUNT_API_TOKEN in the local process.");
  var pages=Math.Clamp(requestedPages,1,3);
  var results=new Dictionary<long,FreelancehuntProject>();
  int fetched=0,raw=0,dropped=0;
  bool paginationKnown=false,next=false;
  for(int page=1;page<=pages;page++)
  {
   ct.ThrowIfCancellationRequested();
   if(fetched>0)await Task.Delay(TimeSpan.FromMilliseconds(1200),ct);
   using var request=new HttpRequestMessage(HttpMethod.Get,PageUri(page,skillIds));
   request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
   request.Headers.AcceptLanguage.ParseAdd("uk");
   using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
   fetched++;
   if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
    throw new InvalidDataException("Freelancehunt authentication/permission rejected (HTTP "+
     (int)response.StatusCode+"). No retry.");
   if((int)response.StatusCode==429)
    throw new InvalidDataException("Freelancehunt API rate limit (429). Scan stopped without retry.");
   response.EnsureSuccessStatusCode();
   var parsed=ParsePage(await response.Content.ReadAsStringAsync(ct));
   raw+=parsed.RawCount;dropped+=parsed.DroppedCount;
   foreach(var project in parsed.Projects)results[project.Id]=project;
   paginationKnown=parsed.NextKnown;next=parsed.HasNext;
   progress?.Invoke($"Freelancehunt page {page}: raw={parsed.RawCount}, dropped={parsed.DroppedCount}, unique={results.Count}");
   if(!paginationKnown || !next)break;
  }
  var ranked=results.Values.Select(Rank).OrderByDescending(x=>x.Score)
   .ThenByDescending(x=>x.Project.PublishedAt).ThenByDescending(x=>x.Project.Id).ToArray();
  foreach(var item in ranked)
  {
   ct.ThrowIfCancellationRequested();
   await output.WriteLineAsync(JsonlOutput.Serialize(item).AsMemory(),ct);
  }
  await output.FlushAsync(ct);
  return new FreelancehuntReport(fetched,raw,dropped,ranked.Length,
   ranked.Count(x=>x.Bucket=="Priority"),ranked.Count(x=>x.Bucket=="Review"),
   ranked.Count(x=>x.Bucket=="LowMatch"),
   next||!paginationKnown||dropped>0,"PARTIAL");
 }
}

public sealed record FreelancehuntProject(
 long Id,string Url,string Title,string Description,
 decimal? Budget,string? Currency,IReadOnlyList<string> Skills,
 int? BidCount,bool? PlusOnly,bool? RemoteOnly,
 DateTimeOffset? PublishedAt,string? Status);

public sealed record FreelancehuntCandidate(
 FreelancehuntProject Project,string Bucket,int Score,IReadOnlyList<string> Reasons);

public sealed record FreelancehuntPage(
 IReadOnlyList<FreelancehuntProject> Projects,int RawCount,int DroppedCount,
 bool HasNext,bool NextKnown);

public sealed record FreelancehuntReport(
 int PagesFetched,int RawRecords,int DroppedRecords,int UniqueProjects,
 int Priority,int Review,int LowMatch,bool IncompleteEnumeration,string CoverageStatus);
