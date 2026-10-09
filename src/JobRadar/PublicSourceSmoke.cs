using System.Text.Json;
namespace JobRadar;

/// <summary>Bounded diagnostics of two sources whose live availability is unproven.</summary>
public static class PublicSourceSmoke
{
 public static async Task<int> RunAsync(IJobSource source,RadarOptions options,CancellationToken ct)
 {
  using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds)};
  http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
  var fetcher=new HttpFetcher(http,Math.Max(1000,options.DelayMilliseconds));
  var report=new Dictionary<string,object?>{["source"]=source.Name,["url"]=source.ListingUrl(0)};
  try
  {
   var raw=await fetcher.GetAsync(source.ListingUrl(0),ct);
   var jobs=source.ParseListings(raw);
   report["found"]=jobs.Count;
   report["coverage"]="UNVERIFIED";
   if(jobs.Count==0)throw new InvalidDataException("No vacancy IDs parsed; source may be empty or changed");
   var sample=jobs[0];
   var detail=await fetcher.GetAsync(sample.Url,ct);
   var text=Parsers.Description(source.Name,detail);
   report["sampleUrl"]=sample.Url;
   report["detailLength"]=text.Length;
   if(text.Length<80)throw new InvalidDataException("Full text not available on source detail page");
   report["status"]="PASS";
   Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
   return 0;
  }
  catch(Exception ex) when(!ct.IsCancellationRequested)
  {
   report["status"]="FAIL";
   report["error"]=ex.GetType().Name+": "+ex.Message;
   Console.Error.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
   return 4;
  }
 }
}
