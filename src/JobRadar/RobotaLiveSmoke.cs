using System.Text.Json;
namespace JobRadar;
/// <summary>
/// Public API integration smoke: search listing and full-text retrieval.
/// Kept separate from deterministic tests; failures flag source incompatibility.
/// </summary>
public static class RobotaLiveSmoke
{
 public static async Task<int> RunAsync(RadarOptions options,CancellationToken ct)
 {
  using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds)};
  http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
  var fetcher=new HttpFetcher(http,Math.Max(1000,options.DelayMilliseconds));
  var result=new Dictionary<string,object?>();
  try
  {
   var source=new RobotaApiSource(".net");
   var payload=await fetcher.GetAsync(source.ListingUrl(0),ct);
   var found=source.ParseListings(payload);
   result["source"]="Robota.ua";
   result["listingCount"]=found.Count;
   result["reportedTotal"]=source.ReportedTotal(payload);
   if(found.Count==0)throw new InvalidDataException("Search API returned zero extracted vacancies");
   var sample=new JobRef(source.Name,
     "https://robota.ua/company307419/vacancy11287397",
     "Backend Engineer (.NET, Agentic Engineering)","КРЕДІ АГРІКОЛЬ БАНК",null);
   var detail=await new RobotaCompanyDetails(fetcher).GetAsync(sample,ct);
   result["sampleUrl"]=sample.Url;
   result["fullTextChars"]=detail.Description.Length;
   result["publicationDate"]=detail.PublishedAt?.ToString("yyyy-MM-dd");
   result["coverage"]="UNVERIFIED"; // One search result and one company != exhaustive coverage
   if(detail.Description.Length<150)throw new InvalidDataException("API detail contained fewer than 150 characters");
   result["status"]="PASS";
   Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
   return 0;
  }
  catch(Exception e) when(!ct.IsCancellationRequested)
  {
   result["status"]="FAIL";
   result["error"]=e.GetType().Name+": "+e.Message;
   Console.Error.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
   return 4;
  }
 }
}