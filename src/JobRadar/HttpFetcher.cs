using System.Net;
namespace JobRadar;
public sealed class HttpFetcher(HttpClient client,int delayMilliseconds)
{
 public async Task<string> GetAsync(string uri,CancellationToken ct)
 {
  for(int attempt=0;attempt<3;attempt++)
  {
   await Task.Delay(Math.Max(0,delayMilliseconds),ct);
   try
   {
    using var resp=await client.GetAsync(uri,ct);
    if(resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
      throw new InvalidDataException("Non-retryable HTTP status "+(int)resp.StatusCode+" for "+uri);
    if((int)resp.StatusCode==429 || (int)resp.StatusCode>=500)
    {
     if(attempt<2)
     {
      var serverWait=resp.Headers.RetryAfter?.Delta;
      var delay=serverWait.HasValue && serverWait.Value>TimeSpan.Zero
          ? TimeSpan.FromSeconds(Math.Min(120,serverWait.Value.TotalSeconds))
          : TimeSpan.FromSeconds(3*(attempt+1));
      await Task.Delay(delay,ct);
      continue;
     }
    }
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadAsStringAsync(ct);
   }
   catch(HttpRequestException) when(attempt<2)
   {
    await Task.Delay(TimeSpan.FromSeconds(3*(attempt+1)),ct);
   }
  }
  throw new InvalidOperationException("HTTP fetch exhausted for "+uri);
 }
}