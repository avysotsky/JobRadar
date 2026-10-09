using System.Text.Json;

namespace JobRadar;

/// <summary>
/// Bounded public-source diagnostic for supplementary RSS filters.
/// No login, no persistent database, and no claims of exhaustive coverage.
/// </summary>
public static class FeedVariantSmoke
{
 public static async Task<int> RunAsync(RadarOptions options,CancellationToken ct)
 {
  using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds)};
  http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
  var fetch=new HttpFetcher(http,Math.Max(options.DelayMilliseconds,1000));
  var sources=new IJobSource[]{new DouKeywordFeed("C#"),new DjinniKeywordFeed("C#")};
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var result=new List<object>();
  bool failed=false;
  foreach(var source in sources)
  {
   try
   {
    var xml=await fetch.GetAsync(source.ListingUrl(0),ct);
    var jobs=source.ParseListings(xml);
    if(jobs.Count==0)throw new InvalidDataException("Feed contained no usable vacancy references");
    var fresh=jobs.Count(j=>seen.Add(j.Source+":"+j.Url));
    result.Add(new {source=source.Name,entries=jobs.Count,uniqueAdded=fresh,
      firstUrl=jobs[0].Url,status="PASS_UNVERIFIED_COVERAGE"});
   }
   catch(Exception ex) when(!ct.IsCancellationRequested)
   {
    failed=true;
    result.Add(new {source=source.Name,entries=0,uniqueAdded=0,
      firstUrl=(string?)null,status="FAIL",error=ex.GetType().Name+": "+ex.Message});
   }
  }
  Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
  return failed?4:0;
 }
}
