using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>
/// One bounded, attribution-preserving Remotive API request, guarded by a
/// durable UTC quota. Never performs vacancy detail requests or asserts
/// provider-confirmed open status from an API listing.
/// </summary>
public static class RemotiveCoreRunner
{
 private static readonly Regex TargetStack=new(
  @"(?<![a-z0-9])(?:asp\.net|\.net|dotnet|c#|csharp|c[- ]sharp)(?![a-z0-9])",
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);

 private const int MaxResponseBytes=8*1024*1024;

 public static async Task<SourceResult> RunAsync(HttpClient client,Storage store,
  DateTimeOffset now,CancellationToken ct)
 {
  // Reserve before ANY network request, including failed and timed-out calls.
  if(!await store.TryReserveRemotiveAsync(now,ct))
   return new SourceResult("remotive",0,0,0,0,null,
    "SKIPPED_QUOTA",null);

  try
  {
   using var request=new HttpRequestMessage(HttpMethod.Get,RemoteBoards.RemotiveApi);
   using var response=await client.SendAsync(request,
    HttpCompletionOption.ResponseHeadersRead,ct);
   if(response.StatusCode is HttpStatusCode.TooManyRequests or
      HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
    throw new InvalidDataException("Remotive provider blocked request");
   if(response.StatusCode!=HttpStatusCode.OK)
    throw new InvalidDataException("Unexpected Remotive HTTP status");
   if(response.Content.Headers.ContentLength is >MaxResponseBytes)
    throw new InvalidDataException("Remotive payload exceeds safe limit");
   await using var stream=await response.Content.ReadAsStreamAsync(ct);
   using var buffer=new MemoryStream();
   var bytes=new byte[32768];
   while(true)
   {
    var n=await stream.ReadAsync(bytes,ct);
    if(n==0)break;
    if(buffer.Length+n>MaxResponseBytes)
     throw new InvalidDataException("Remotive payload exceeds safe limit");
    buffer.Write(bytes,0,n);
   }
   var json=new UTF8Encoding(false,true).GetString(buffer.ToArray());
   var page=RemoteBoards.ParseRemotive(json);
   if(page.RawCount==0)
    return new SourceResult("remotive",1,0,0,0,null,
     "PARTIAL","EMPTY_UNVERIFIED",RawRecords:0,DroppedRecords:page.DroppedCount);

   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   var accepted=0;
   foreach(var opening in page.Items)
   {
    ct.ThrowIfCancellationRequested();
    // Relevant .NET snapshots only, so the finite triage export isn't
    // flooded by unrelated languages. This is not a claim of full coverage.
    if(!TargetStack.IsMatch(opening.Title+" "+(opening.FullDescription??opening.Excerpt)))
     continue;
    if(!seen.Add(opening.Url))continue;
    var snapshot=new JobRef("remotive",opening.Url,opening.Title,
     opening.Company,opening.PublishedAt,
     string.IsNullOrWhiteSpace(opening.Excerpt)?null:opening.Excerpt,
     opening.CandidateLocation);
    await store.SaveDiscovered(snapshot,ct,"remotive-software-dev");
    accepted++;
   }
   return new SourceResult("remotive",1,accepted,0,0,null,
    "PARTIAL",page.DroppedCount>0?"UNUSABLE_PROVIDER_URLS":null,
    RawRecords:page.RawCount,DroppedRecords:page.DroppedCount);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch(Exception ex)
  {
   // Do not retain arbitrary API payloads, HTTP paths, or potentially
   // user-controlled exception strings in persistent crawl reports.
   return new SourceResult("remotive",0,0,0,0,null,
    "FAILED",ex.GetType().Name);
  }
 }
}
