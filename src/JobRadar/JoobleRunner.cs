using System.Text.Json;
namespace JobRadar;

/// <summary>
/// Opt-in single-page Jooble discovery. Never scheduled automatically: the
/// free regional key has a 500-request lifetime quota, including failed calls.
/// </summary>
public static class JoobleRunner
{
 public static async Task<int> RunAsync(Storage storage,RadarOptions settings,CancellationToken ct)
 {
  var key=Environment.GetEnvironmentVariable("JOOBLE_API_KEY");
  var priorRaw=Environment.GetEnvironmentVariable("JOOBLE_API_PRIOR_USED");
  var maxRaw=Environment.GetEnvironmentVariable("JOOBLE_MAX_NEW_REQUESTS");
  if(string.IsNullOrWhiteSpace(key)||
     !int.TryParse(priorRaw,out var prior)||prior is <0 or >500 ||
     !int.TryParse(maxRaw,out var budget)||budget is <1 or >500)
  {
   Console.Error.WriteLine("Jooble disabled: set JOOBLE_API_KEY, JOOBLE_API_PRIOR_USED (0..500) and JOOBLE_MAX_NEW_REQUESTS (1..500) explicitly. Never commit your key.");
   return 2;
  }
  if(settings.JoobleQueries.Length==0)
  {
   Console.Error.WriteLine("No JoobleQueries configured.");
   return 2;
  }
  using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(settings.TimeoutSeconds)};
  http.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
  JoobleApi api;
  try { api=new JoobleApi(http,key); }
  catch(ArgumentException e){Console.Error.WriteLine(e.Message);return 2;}
  var found=0;var expected=0;var attempts=0;var errors=new List<string>();
  foreach(var keywords in settings.JoobleQueries.Distinct(StringComparer.OrdinalIgnoreCase))
  {
   int used;
   try { used=await storage.ReserveJoobleCallAsync(prior,budget,ct); }
   catch(InvalidOperationException)
   { errors.Add("Quota exhausted; no further Jooble requests were made");break; }
   attempts++;
   try
   {
    // Only page 1 to conserve the lifetime quota. Coverage is always PARTIAL.
    var page=await api.SearchAsync(keywords,settings.JoobleLocation,1,ct);
    expected+=page.TotalCount;
    foreach(var job in page.Jobs)
    {
     await storage.SaveDiscovered(job,ct,"jooble:"+keywords);
     found++;
    }
    Console.WriteLine("Jooble query '"+keywords+"': "+page.Jobs.Count+
      " preview-only jobs, API total "+page.TotalCount+"; lifetime reservation "+used);
   }
   catch(Exception ex) when(!ct.IsCancellationRequested)
   {
    var summary=ex.GetType().Name+": "+ex.Message;
    // Do not include key-bearing API endpoint in storage/logging.
    await storage.SaveError("jooble","jooble-query:"+keywords,summary,ct);
    errors.Add("Query '"+keywords+"' failed: "+summary);
   }
  }
  var report=new{source="jooble",status="PARTIAL_SNIPPET_ONLY",
    requestsReserved=attempts,referencesSaved=found,
    sumOfQueryTotals=expected,errors};
  Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
  return errors.Count==0?0:4;
 }
}
