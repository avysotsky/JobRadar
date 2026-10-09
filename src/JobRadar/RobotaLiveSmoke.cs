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
  var source=new RobotaApiSource(".net");
  var result=new Dictionary<string,object?>();
  try
  {
   string html=await fetcher.GetAsync(source.ListingUrl(0),ct);
   var jobs=source.ParseListings(html);
   result["searchUrl"]=source.ListingUrl(0);
   result["listingCount"]=jobs.Count;
   result["reportedTotal"]=source.ReportedTotal(html);
   using(var api=JsonDocument.Parse(html))
   {
    if(api.RootElement.TryGetProperty("documents",out var d) && d.GetArrayLength()>0)
    {
     var sample=d[0];
     result["apiFields"]=sample.EnumerateObject().Select(x=>x.Name).ToArray();
     foreach(var field in new[]{"shortDescription","description","fullDescription"})
       if(sample.TryGetProperty(field,out var v))
         result["api:"+field+"Chars"]=v.ToString().Length;
    }
   }
   if(jobs.Count==0)throw new InvalidDataException("No Robota vacancy anchors extracted.");
   // This explicit fixture was visible publicly on 2026-10-09. It is only a smoke
   // example; the live run does NOT establish search completeness.
   var sampleUrl="https://robota.ua/company307419/vacancy11287397";
   var detail=await fetcher.GetAsync(sampleUrl,ct);
   var description=Parsers.Description(source.Name,detail);
   result["sampleUrl"]=sampleUrl;
   result["descriptionChars"]=description.Length;
   if(description.Length<150)
   {
    foreach(var probeUrl in new[]{
      "https://api.rabota.ua/vacancy/11287397",
      "https://api.robota.ua/vacancies/11287397" })
    {
     try
     {
      var json=await fetcher.GetAsync(probeUrl,ct);
      using var parsed=JsonDocument.Parse(json);
      var keys=parsed.RootElement.ValueKind==JsonValueKind.Object
       ? parsed.RootElement.EnumerateObject().Select(p=>p.Name).Take(25).ToArray()
       : [];
      result["probe:"+probeUrl]=new{size=json.Length,keys};
     }
     catch(Exception exception) when(exception is not OperationCanceledException)
     {
      result["probe:"+probeUrl]=exception.GetType().Name+": "+exception.Message;
     }
    }
   }
   if(description.Length<150)
   {
    const string companyEndpoint="https://api.robota.ua/companies/307419/published-vacancies";
    try
    {
     var payload=await fetcher.GetAsync(companyEndpoint,ct);
     using var companyJson=JsonDocument.Parse(payload);
     var keys=companyJson.RootElement.ValueKind==JsonValueKind.Object
       ? companyJson.RootElement.EnumerateObject().Select(e=>e.Name).Take(30).ToArray()
       : [];
     result["companyApi"]=new{size=payload.Length,keys};
    }
    catch(Exception exception) when(exception is not OperationCanceledException)
    {
     result["companyApi"]=exception.GetType().Name+": "+exception.Message;
    }
   }
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
