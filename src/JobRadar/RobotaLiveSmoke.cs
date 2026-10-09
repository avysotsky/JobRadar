using System.Text.Json;

namespace JobRadar;

/// <summary>
/// Runs a bounded, unauthenticated verification against two public Robota pages.
/// No database, credentials, browser emulation or anti-bot workarounds are used.
/// </summary>
public static class RobotaLiveSmoke
{
 public static async Task<int> RunAsync(RadarOptions options, CancellationToken ct)
 {
  using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds)};
  client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
  var fetcher=new HttpFetcher(client,Math.Max(1000,options.DelayMilliseconds));
  var source=new RobotaSource(".net");
  var result=new Dictionary<string,object?>();
  try
  {
   string html=await fetcher.GetAsync(source.ListingUrl(0),ct);
   var jobs=source.ParseListings(html);
   result["searchUrl"]=source.ListingUrl(0);
   result["listingCount"]=jobs.Count;
   result["reportedTotal"]=Parsers.ReportedTotal(html);
   if(jobs.Count==0)throw new InvalidDataException("No Robota vacancy anchors extracted.");
   // This explicit fixture was visible publicly on 2026-10-09. It is only a smoke
   // example; the live run does NOT establish search completeness.
   var sampleUrl="https://robota.ua/company307419/vacancy11287397";
   var detail=await fetcher.GetAsync(sampleUrl,ct);
   var description=Parsers.Description(source.Name,detail);
   result["sampleUrl"]=sampleUrl;
   result["descriptionChars"]=description.Length;
   result["publishedDate"]=RobotaParser.PublishedDate(detail)?.ToString("yyyy-MM-dd");
   result["coverage"]="UNVERIFIED";
   if(description.Length<150)throw new InvalidDataException("Vacancy detail extraction returned under 150 chars.");
   result["status"]="PASS";
   Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
   return 0;
  }
  catch(Exception ex) when(ex is not OperationCanceledException)
  {
   result["status"]="FAIL";
   result["error"]=ex.GetType().Name+": "+ex.Message;
   Console.Error.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
   return 4;
  }
 }
}
