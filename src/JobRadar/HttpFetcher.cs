using System.Net;
namespace JobRadar;
public sealed class HttpFetcher(HttpClient client,int delayMilliseconds)
{
 public async Task<string> GetAsync(string uri,CancellationToken ct)
 {
  for(int attempt=0;attempt<3;attempt++)
  {
   await Task.Delay(delayMilliseconds,ct);
   try
   {
    using var resp=await client.GetAsync(uri,ct);
    if(resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound) throw new HttpRequestException($"Blocked or missing: {(int)resp.StatusCode} {uri}");
    if((int)resp.StatusCode==429 || (int)resp.StatusCode>=500)
    {
     if(attempt<2){await Task.Delay(TimeSpan.FromSeconds(3*(attempt+1)),ct);continue;}
    }
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadAsStringAsync(ct);
   }
   catch(HttpRequestException) when(attempt<2){await Task.Delay(TimeSpan.FromSeconds(3*(attempt+1)),ct);}
  }
  throw new InvalidOperationException($"Fetch exhausted for {uri}");
 }
}