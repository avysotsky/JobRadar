using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using AngleSharp.Html.Parser;

namespace JobRadar;

/// <summary>
/// Bounded, manual and read-only remote-job discovery from officially published
/// WWR RSS and Remotive public API. Never silently claims Ukraine eligibility.
/// </summary>
public static class RemoteBoards
{
 public const string WwrBackend="https://weworkremotely.com/categories/remote-back-end-programming-jobs.rss";
 public const string WwrProgramming="https://weworkremotely.com/categories/remote-programming-jobs.rss";
 public const string RemotiveApi="https://remotive.com/api/remote-jobs?category=software-dev&limit=250";

 public static RemoteBoardPage ParseWwr(string rss,string source)
 {
  var doc=XDocument.Parse(rss);
  var items=doc.Descendants().Where(x=>x.Name.LocalName=="item").ToArray();
  var result=new List<RemoteBoardOpening>();int dropped=0;
  foreach(var item in items)
  {
   var rawUrl=Element(item,"link");
   if(!ValidUrl(rawUrl,"weworkremotely.com",out var link)){dropped++;continue;}
   var title=Parsers.Clean(Element(item,"title"));
   if(title.Length<3){dropped++;continue;}
   DateTimeOffset? published=null;
   if(DateTimeOffset.TryParse(Element(item,"pubDate"),CultureInfo.InvariantCulture,
       DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var date))published=date;
   // WWR RSS is not a verified complete vacancy, but may contain far more
   // than the 950 characters we show in compact JSONL. Analyze everything
   // received so late .NET, B2 and location restrictions are not missed.
   var receivedText=Plain(Element(item,"description"),int.MaxValue);
   var excerpt=receivedText.Length>950?receivedText[..950]+"…":receivedText;
   result.Add(new RemoteBoardOpening(source,link!,title,null,excerpt,
    published,null,null,null,false,"We Work Remotely")
    {FullDescription=receivedText});
  }
  return new RemoteBoardPage(result,items.Length,dropped);
 }

 public static RemoteBoardPage ParseRemotive(string json)
 {
  using var doc=JsonDocument.Parse(json);
  var root=doc.RootElement;
  if(root.ValueKind!=JsonValueKind.Object||
     !root.TryGetProperty("jobs",out var items)||
     items.ValueKind!=JsonValueKind.Array)
   throw new InvalidDataException("Remotive API response missing jobs array");
  var result=new List<RemoteBoardOpening>();int raw=0,dropped=0;
  foreach(var item in items.EnumerateArray())
  {
   raw++;
   var url=String(item,"url");
   if(!ValidUrl(url,"remotive.com",out var link)){dropped++;continue;}
   var title=String(item,"title");
   if(string.IsNullOrWhiteSpace(title)){dropped++;continue;}
   DateTimeOffset? published=null;
   if(DateTimeOffset.TryParse(String(item,"publication_date"),CultureInfo.InvariantCulture,
     DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var date))published=date;
   // Rank against the COMPLETE received description; export only a bounded
   // excerpt. A late B2/onsite restriction must never be lost to truncation.
   var fullDescription=Plain(String(item,"description"),int.MaxValue);
   var excerpt=fullDescription.Length>2400?fullDescription[..2400]+"…":fullDescription;
   result.Add(new RemoteBoardOpening("remotive",link!,title.Trim(),
    String(item,"company_name"),excerpt,published,
    String(item,"candidate_required_location"),String(item,"job_type"),
    String(item,"salary"),fullDescription.Length>0,"Remotive")
    {FullDescription=fullDescription});
  }
  return new RemoteBoardPage(result,raw,dropped);
 }

 public static RemoteBoardCandidate Assess(RemoteBoardOpening item)
 {
  // Board metadata indicates remote work but NEVER establishes eligibility
  // to work remotely from Ukraine without checking the location requirement.
  var body="Remote work. "+(item.FullDescription??item.Excerpt);
  var snapshot=new VacancySnapshot(item.Source,item.Url,item.Title,item.Company,
   item.HasFullText?null:body,item.HasFullText?body:"",
   item.HasFullText,null,item.PublishedAt,DateTimeOffset.UtcNow,
   [item.Source]);
  var a=VacancyTriage.Assess(snapshot);
  var warnings=a.Warnings.ToList();
  var bucket=a.Bucket;
  if(item.Source=="remotive")
  {
   var location=item.CandidateLocation?.Trim();
   // Never treat 'Worldwide except Ukraine' or 'Ukraine not eligible' as
   // approval merely because a country name appears in the field.
   if(string.IsNullOrWhiteSpace(location)||
      !(location.Equals("Worldwide",StringComparison.OrdinalIgnoreCase)||
        location.Equals("Anywhere",StringComparison.OrdinalIgnoreCase)||
        location.Equals("Global",StringComparison.OrdinalIgnoreCase)||
        location.Equals("Ukraine",StringComparison.OrdinalIgnoreCase)||
        location.Equals("Ukraine only",StringComparison.OrdinalIgnoreCase)))
   {
    warnings.Add("Remote eligibility from Ukraine is unverified; provider location: "+
       (string.IsNullOrWhiteSpace(location)?"unspecified":location));
    if(bucket==FitBucket.LikelyFit)bucket=FitBucket.NeedsReview;
   }
  }
  if(item.Source.StartsWith("wwr-",StringComparison.Ordinal))
  {
   warnings.Add("WWR RSS excerpt is not provider-verified complete text or geographic eligibility");
   if(bucket==FitBucket.LikelyFit)bucket=FitBucket.NeedsReview;
  }
  var review=RemoteReviewTriage.Assess(item,bucket);
  return new RemoteBoardCandidate(item,bucket,a.Score,a.Reasons,warnings)
   {ReviewPriority=review.Priority,ReviewEvidence=review.Evidence};
 }

 public static async Task<RemoteBoardsReport> ScanAsync(
  HttpClient client,TextWriter output,Action<string>? progress,CancellationToken ct)
 {
  var endpoints=new (string Source,string Url)[]
  {
   ("wwr-backend",WwrBackend),
   ("wwr-programming",WwrProgramming),
   ("remotive",RemotiveApi)
  };
  var counts=new List<RemoteBoardSourceStatus>();
  var found=new Dictionary<string,RemoteBoardOpening>(StringComparer.OrdinalIgnoreCase);
  foreach(var (source,url) in endpoints)
  {
   ct.ThrowIfCancellationRequested();
   if(counts.Count>0)await Task.Delay(TimeSpan.FromMilliseconds(1200),ct);
   try
   {
    using var req=new HttpRequestMessage(HttpMethod.Get,url);
    using var response=await client.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);
    if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden||
       (int)response.StatusCode==429)
     throw new InvalidDataException("HTTP "+(int)response.StatusCode+" (no retry)");
    response.EnsureSuccessStatusCode();
    var payload=await response.Content.ReadAsStringAsync(ct);
    var page=source=="remotive"?ParseRemotive(payload):ParseWwr(payload,source);
    foreach(var item in page.Items)
    {
     if(!found.ContainsKey(item.Url))found[item.Url]=item;
    }
    counts.Add(new RemoteBoardSourceStatus(source,page.RawCount,page.DroppedCount,
     "PARTIAL",null));
    progress?.Invoke($"{source}: returned={page.RawCount}, dropped={page.DroppedCount}, unique_total={found.Count}");
   }
   catch(Exception e) when(e is not OperationCanceledException)
   {
    counts.Add(new RemoteBoardSourceStatus(source,0,0,"FAILED",e.Message));
    progress?.Invoke($"{source}: FAILED, {e.GetType().Name} ({e.Message})");
   }
  }
  var evaluated=found.Values.Select(Assess).OrderBy(x=>x.Bucket)
   .ThenBy(x=>x.ReviewPriority).ThenByDescending(x=>x.Score)
   .ThenByDescending(x=>x.Opening.PublishedAt).ToArray();
  foreach(var item in evaluated)
  {
   ct.ThrowIfCancellationRequested();
   await output.WriteLineAsync(JsonlOutput.Serialize(item).AsMemory(),ct);
  }
  await output.FlushAsync(ct);
  return new RemoteBoardsReport(counts,evaluated.Length,
   evaluated.Count(x=>x.Bucket==FitBucket.LikelyFit),
   evaluated.Count(x=>x.Bucket==FitBucket.NeedsReview),
   evaluated.Count(x=>x.Bucket==FitBucket.Excluded),
   counts.Any(x=>x.Status=="FAILED"||x.DroppedRecords>0)?"FAILED_OR_DROPPED":"PARTIAL")
   {ReviewBreakdown=RemoteReviewTriage.Summarize(evaluated)};
 }

 private static string? Element(XElement item,string name)=>
  item.Elements().FirstOrDefault(x=>x.Name.LocalName==name)?.Value;

 private static string? String(JsonElement obj,string name)=>
  obj.ValueKind==JsonValueKind.Object&&obj.TryGetProperty(name,out var x)&&
  x.ValueKind==JsonValueKind.String?x.GetString():null;

 private static string Plain(string? html,int maxChars)
 {
  if(string.IsNullOrWhiteSpace(html))return "";
  var doc=new HtmlParser().ParseDocument(html);
  var text=Parsers.Clean(doc.Body?.TextContent??html);
  return text.Length>maxChars?text[..maxChars]+"…":text;
 }

 private static bool ValidUrl(string? raw,string host,out string? value)
 {
  value=null;
  if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri) ||
     uri.Scheme!=Uri.UriSchemeHttps ||
     !(uri.Host.Equals(host,StringComparison.OrdinalIgnoreCase)||
       uri.Host.Equals("www."+host,StringComparison.OrdinalIgnoreCase)))
    return false;
  if(!uri.AbsolutePath.StartsWith("/remote-jobs/",StringComparison.OrdinalIgnoreCase))
   return false;
  value=uri.GetLeftPart(UriPartial.Path);
  return true;
 }
}

public sealed record RemoteBoardOpening(
 string Source,string Url,string Title,string? Company,string Excerpt,
 DateTimeOffset? PublishedAt,string? CandidateLocation,string? JobType,
 string? Salary,bool HasFullText,string Attribution)
{
 // Entire received RSS/API text is used only in-process for ranking, never
 // serialized to compact JSONL or persisted. WWR RSS remains unverified as
 // a complete vacancy even when the entire RSS description was analyzed.
 [JsonIgnore]
 public string? FullDescription {get;init;}
}

public sealed record RemoteBoardCandidate(
 RemoteBoardOpening Opening,FitBucket Bucket,int Score,
 IReadOnlyList<string> Reasons,IReadOnlyList<string> Warnings)
{
 // Independent queue priority, NOT evidence that a job is open or that a
 // candidate may legally work from Ukraine. Older JSONL lacks these fields.
 public RemoteReviewPriority ReviewPriority {get;init;}=RemoteReviewPriority.NotApplicable;
 public IReadOnlyList<string> ReviewEvidence {get;init;}=[];
}

public sealed record RemoteBoardPage(
 IReadOnlyList<RemoteBoardOpening> Items,int RawCount,int DroppedCount);

public sealed record RemoteBoardSourceStatus(
 string Source,int RawRecords,int DroppedRecords,string Status,string? Error);

public sealed record RemoteBoardsReport(
 IReadOnlyList<RemoteBoardSourceStatus> Sources,int UniqueJobs,
 int LikelyFit,int NeedsReview,int Excluded,string CoverageStatus)
{
 public RemoteReviewBreakdown ReviewBreakdown {get;init;}=new(0,0,0,0,0,0);
}
