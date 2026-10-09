using System.Text.Json;

namespace JobRadar;

/// <summary>
/// Probe a real multi-page public API query without changing or requiring
/// a database. It makes at most three search requests and reports ID overlap.
/// </summary>
public static class RobotaPagingSmoke
{
 public static async Task<int> RunAsync(RadarOptions options,CancellationToken ct)
 {
  using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds)};
  http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
  var fetcher=new HttpFetcher(http,Math.Max(options.DelayMilliseconds,1000));
  var source=new RobotaApiSource("менеджер");
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var pages=new List<object>();
  var total=-1;
  var error="";
  try
  {
   for(int p=0;p<3;p++)
   {
    var response=await fetcher.GetAsync(source.ListingUrl(p),ct);
    var batch=source.ParseListings(response);
    var reported=source.ReportedTotal(response);
    if(reported is null)throw new InvalidDataException("Missing or malformed source total on page "+p);
    total=total<0?reported.Value:total;
    if(total!=reported.Value)throw new InvalidDataException("API total differs across page snapshots");
    var raw=source.RawDocumentCount(response);
    var overlap=0;
    foreach(var item in batch)if(!seen.Add(item.Url))overlap++;
    pages.Add(new {page=p,received=batch.Count,rawRecords=raw,droppedRecords=raw-batch.Count,overlap,unique=seen.Count,
     firstId=batch.FirstOrDefault()?.Url,lastId=batch.LastOrDefault()?.Url});
    if(batch.Count==0 || seen.Count>=total)break;
   }
   if(total>59 && pages.Count<2)throw new InvalidDataException("Multi-page query returned fewer than two pages");
   if(total>59 && seen.Count<=59)throw new InvalidDataException("No evidence next page advances beyond first 59 records");
   var result=new {status="PASS",query="менеджер",total,uniqueIds=seen.Count,pages,
       coverage=seen.Count==total?"QUERY_ID_RECONCILED":"PARTIAL_SAMPLE_ONLY"};
   Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
   return 0;
  }
  catch(Exception ex) when(!ct.IsCancellationRequested)
  {
   error=ex.GetType().Name+": "+ex.Message;
   Console.Error.WriteLine(JsonSerializer.Serialize(new {status="FAIL",query="backend",total,
      uniqueIds=seen.Count,pages,error},new JsonSerializerOptions{WriteIndented=true}));
   return 4;
  }
 }
}
