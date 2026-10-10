using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
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
 public const string WwrFullstack="https://weworkremotely.com/categories/remote-full-stack-programming-jobs.rss";
 public const string CanonicalWwrSource="wwr";
 private const int WwrResponseMaxBytes=2_000_000;
 private const int RemotiveResponseMaxBytes=12_000_000;

 public static RemoteBoardPage ParseWwr(string rss,string source)
 {
  if(source is not ("wwr-backend" or "wwr-programming" or "wwr-fullstack"))
   throw new ArgumentException("Unknown WWR RSS feed provenance",nameof(source));
  // Prohibit DTD/entity expansion. This is a bounded public RSS parser, not an
  // HTML detail scraper or a second WWR source implementation.
  using var reader=XmlReader.Create(new StringReader(rss),
   new XmlReaderSettings
   {
    DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,
    MaxCharactersInDocument=WwrResponseMaxBytes
   });
  var doc=XDocument.Load(reader);
  if(doc.Root?.Name.LocalName!="rss" ||
     !doc.Root.Elements().Any(x=>x.Name.LocalName=="channel"))
   throw new InvalidDataException("Unsupported WWR RSS envelope");
  var items=doc.Descendants().Where(x=>x.Name.LocalName=="item").ToArray();
  var result=new List<RemoteBoardOpening>();int dropped=0,invalidDates=0;
  var observedAt=DateTimeOffset.UtcNow;
  foreach(var item in items)
  {
   var rawUrl=Element(item,"link");
   if(!ValidUrl(rawUrl,"weworkremotely.com",out var link)){dropped++;continue;}
   var title=Parsers.Clean(Element(item,"title"));
   if(title.Length<3){dropped++;continue;}
   DateTimeOffset? published=null;
   var dateText=Element(item,"pubDate");
   if(!string.IsNullOrWhiteSpace(dateText))
   {
    if(DateTimeOffset.TryParse(dateText,CultureInfo.InvariantCulture,
      DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var date))
     published=date;
    else invalidDates++;
   }
   // WWR RSS is not a verified complete vacancy, but may contain far more
   // than the 950 characters we show in compact JSONL. Analyze everything
   // received so late .NET, B2 and location restrictions are not missed.
   var receivedText=Plain(Element(item,"description"),int.MaxValue);
   var excerpt=receivedText.Length>950?receivedText[..950]+"…":receivedText;
   // Only explicitly named RSS company elements count as company evidence.
   // Never derive an employer from the title or feed category.
   var company=Parsers.Clean(Element(item,"company"));
   result.Add(new RemoteBoardOpening(source,link!,title,
    company.Length==0?null:company,excerpt,
    published,null,null,null,false,"We Work Remotely")
    {FullDescription=receivedText,FeedQueries=[source],ObservedAt=observedAt});
  }
  return new RemoteBoardPage(result,items.Length,dropped)
   {InvalidDates=invalidDates};
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

 // Preserve the original Phase 14 public contract and its exact three requests.
 public static Task<RemoteBoardsReport> ScanAsync(
  HttpClient client,TextWriter output,Action<string>? progress,CancellationToken ct)=>
  ScanCoreAsync(client,output,progress,ct,wwrOnly:false,includeFullstack:false);

 /// <summary>
 /// Explicit, read-only WWR-only RSS scan. Does not request Remotive and
 /// never accesses PostgreSQL. Full-stack is opt-in, off by default.
 /// </summary>
 public static Task<RemoteBoardsReport> ScanWwrAsync(
  HttpClient client,TextWriter output,Action<string>? progress,CancellationToken ct,
  bool includeFullstack=false)=>
  ScanCoreAsync(client,output,progress,ct,wwrOnly:true,includeFullstack:includeFullstack);

 private static async Task<RemoteBoardsReport> ScanCoreAsync(
  HttpClient client,TextWriter output,Action<string>? progress,CancellationToken ct,
  bool wwrOnly,bool includeFullstack)
 {
  var endpoints=new List<(string Source,string Url)>
  {
   ("wwr-backend",WwrBackend),
   ("wwr-programming",WwrProgramming)
  };
  if(wwrOnly && includeFullstack)endpoints.Add(("wwr-fullstack",WwrFullstack));
  if(!wwrOnly)endpoints.Add(("remotive",RemotiveApi));
  var counts=new List<RemoteBoardSourceStatus>();
  var found=new Dictionary<string,RemoteBoardOpening>(StringComparer.OrdinalIgnoreCase);
  int duplicateTotal=0;
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
    var payload=await ReadBoundedUtf8Async(response.Content,
     source=="remotive"?RemotiveResponseMaxBytes:WwrResponseMaxBytes,ct);
    var page=source=="remotive"?ParseRemotive(payload):ParseWwr(payload,source);
    int duplicates=0;
    foreach(var item in page.Items)
    {
     if(found.TryGetValue(item.Url,out var earlier))
     {
      duplicates++;
      // One posting per canonical URL, but retain *every* query that found it.
      // Preserve first source ID to avoid breaking compact Phase 14 JSONL.
      var queries=earlier.FeedQueries.Concat(item.FeedQueries)
       .Distinct(StringComparer.Ordinal).ToArray();
      found[item.Url]=earlier with
      {
       FeedQueries=queries,
       Company=earlier.Company??item.Company,
       PublishedAt=earlier.PublishedAt??item.PublishedAt,
       FullDescription=(earlier.FullDescription?.Length??0)>=(item.FullDescription?.Length??0)
        ?earlier.FullDescription:item.FullDescription,
       Excerpt=earlier.Excerpt.Length>=item.Excerpt.Length?earlier.Excerpt:item.Excerpt
      };
     }
     else found[item.Url]=item;
    }
    duplicateTotal+=duplicates;
    counts.Add(new RemoteBoardSourceStatus(source,page.RawCount,page.DroppedCount,
     "PARTIAL",null)
    {
     ParsedRecords=page.Items.Count,AcceptedRecords=page.Items.Count-duplicates,
     DuplicateRecords=duplicates,InvalidDates=page.InvalidDates,
     CoverageWarning=page.RawCount==0
      ?"Empty feed does not establish zero active vacancies":null
    });
    progress?.Invoke($"{source}: returned={page.RawCount}, parsed={page.Items.Count}, dropped={page.DroppedCount}, duplicates={duplicates}, unique_total={found.Count}");
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
   {
    ReviewBreakdown=RemoteReviewTriage.Summarize(evaluated),
    DuplicateRecords=duplicateTotal
   };
 }

 private static async Task<string> ReadBoundedUtf8Async(
  HttpContent content,int maxBytes,CancellationToken ct)
 {
  if(content.Headers.ContentLength is long length && length>maxBytes)
   throw new InvalidDataException("RSS/API response exceeds configured byte limit");
  await using var stream=await content.ReadAsStreamAsync(ct);
  using var output=new MemoryStream();
  var buffer=new byte[8192];
  while(true)
  {
   var count=await stream.ReadAsync(buffer,ct);
   if(count==0)break;
   if(output.Length+count>maxBytes)
    throw new InvalidDataException("RSS/API response exceeds configured byte limit");
   output.Write(buffer,0,count);
  }
  var bytes=output.ToArray().AsSpan();
  if(bytes.StartsWith(new byte[]{0xEF,0xBB,0xBF}))bytes=bytes[3..];
  return new UTF8Encoding(false,true).GetString(bytes);
 }

 /// <summary>Canonical DB source identity, without writing provider data.</summary>
 public static string CanonicalSource(string source)=>
  source is "wwr-backend" or "wwr-programming" or "wwr-fullstack"
   ? CanonicalWwrSource:source;

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
     uri.Scheme!=Uri.UriSchemeHttps || !uri.IsDefaultPort ||
     !string.IsNullOrEmpty(uri.UserInfo) ||
     !(uri.Host.Equals(host,StringComparison.OrdinalIgnoreCase)||
       uri.Host.Equals("www."+host,StringComparison.OrdinalIgnoreCase)))
    return false;
  if(!uri.AbsolutePath.StartsWith("/remote-jobs/",StringComparison.OrdinalIgnoreCase)||
     uri.AbsolutePath.Length<="/remote-jobs/".Length ||
     uri.AbsolutePath.Contains("%2f",StringComparison.OrdinalIgnoreCase) ||
     uri.AbsolutePath.Contains("%5c",StringComparison.OrdinalIgnoreCase))
   return false;
  // Strip ref/utm query and fragment, normalize www alias and trailing slash.
  // The public provider page remains the only job application destination.
  var builder=new UriBuilder(uri){Host=host,Query="",Fragment=""};
  value=builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
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
 // Backwards compatible additions: Source remains the original feed ID
 // in existing JSONL, while CanonicalSource is the future DB identity.
 public string CanonicalSource=>RemoteBoards.CanonicalSource(Source);
 public IReadOnlyList<string> FeedQueries {get;init;}=[];
 public DateTimeOffset? ObservedAt {get;init;}
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
 IReadOnlyList<RemoteBoardOpening> Items,int RawCount,int DroppedCount)
{
 public int InvalidDates {get;init;}
}

public sealed record RemoteBoardSourceStatus(
 string Source,int RawRecords,int DroppedRecords,string Status,string? Error)
{
 public int ParsedRecords {get;init;}
 public int AcceptedRecords {get;init;}
 public int DuplicateRecords {get;init;}
 public int InvalidDates {get;init;}
 public string? CoverageWarning {get;init;}
}

public sealed record RemoteBoardsReport(
 IReadOnlyList<RemoteBoardSourceStatus> Sources,int UniqueJobs,
 int LikelyFit,int NeedsReview,int Excluded,string CoverageStatus)
{
 public RemoteReviewBreakdown ReviewBreakdown {get;init;}=new(0,0,0,0,0,0);
 public int DuplicateRecords {get;init;}
}
