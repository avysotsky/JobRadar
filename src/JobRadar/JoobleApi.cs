using System.Net.Http.Json;
using System.Text.Json;
namespace JobRadar;

/// <summary>
/// Official regional Jooble REST API client. Never logs an endpoint with the API key.
/// All responses contain discovery snippets, not validated complete descriptions.
/// </summary>
public sealed class JoobleApi(HttpClient client, string apiKey)
{
 private readonly string endpoint=CreateEndpoint(apiKey);
 private static string CreateEndpoint(string key)
 {
  if(string.IsNullOrWhiteSpace(key) || key.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))
    throw new ArgumentException("JOOBLE_API_KEY must contain a nonempty opaque key",nameof(key));
  return "https://ua.jooble.org/api/"+key;
 }

 public async Task<JoobleSearchResult> SearchAsync(string keywords,string location,int page,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(keywords)||string.IsNullOrWhiteSpace(location)||page<1)
    throw new ArgumentException("Search keywords, location and a positive page are required");
  var body=new Dictionary<string,object>{
   ["keywords"]=keywords,["location"]=location,["page"]=page,
   ["ResultOnPage"]=20,["companysearch"]=false
  };
  // Keep URL/key out of error messages; count reservation happens before this call.
  using var request=new HttpRequestMessage(HttpMethod.Post,endpoint){
   Content=JsonContent.Create(body)
  };
  using var response=await client.SendAsync(request,ct);
  if(!response.IsSuccessStatusCode)
   throw new HttpRequestException("Jooble API returned HTTP "+(int)response.StatusCode);
  var json=await response.Content.ReadAsStringAsync(ct);
  return ParseResponse(json);
 }

 public static JoobleSearchResult ParseResponse(string json)
 {
  using var doc=JsonDocument.Parse(json);
  if(!doc.RootElement.TryGetProperty("jobs",out var arr)||arr.ValueKind!=JsonValueKind.Array ||
     !doc.RootElement.TryGetProperty("totalCount",out var count)||!count.TryGetInt32(out var total)||total<0)
   throw new InvalidDataException("Jooble response lacks jobs or totalCount");
  var found=new List<JobRef>();
  foreach(var job in arr.EnumerateArray())
  {
   string? title=Read(job,"title");
   string? url=Read(job,"link");
   if(string.IsNullOrWhiteSpace(title)||string.IsNullOrWhiteSpace(url)||
      !Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||
      string.IsNullOrEmpty(uri.Host))continue;
   var preview=Parsers.Clean(Read(job,"snippet"));
   // 'updated' is NOT a publication date. Leave PublishedAt unknown.
   found.Add(new JobRef("jooble",uri.ToString(),Parsers.Clean(title),
            Parsers.Clean(Read(job,"company")),null,
            preview.Length==0?null:preview));
  }
  return new JoobleSearchResult(total,found.DistinctBy(j=>j.Url).ToArray());
 }
 private static string? Read(JsonElement obj,string name)
  => obj.TryGetProperty(name,out var value) && value.ValueKind!=JsonValueKind.Null?value.ToString():null;
}
public sealed record JoobleSearchResult(int TotalCount,IReadOnlyList<JobRef> Jobs);
