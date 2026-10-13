using System.Net;
using System.Text.RegularExpressions;

namespace JobRadar;

public sealed record StatusOnlyRefreshReport(
 DateTimeOffset StartedAt,DateTimeOffset EndedAt,
 int Selected,int Attempted,int Closed,int Unverified,int Failed,int Skipped,
 string Scope);

/// <summary>
/// A bounded, operator-invoked status-only check. No description collection,
/// no inference of positive opening from HTTP success or a generic apply link.
/// </summary>
public sealed class StatusOnlyRefresh(HttpFetcher fetcher,Storage storage,
 Action<string>? progress=null)
{
 private static readonly Regex DouPath=new(
  @"^/(?:companies/[^/]+/)?vacancies/[0-9]+/?$",
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
 private static readonly Regex DjinniPath=new(
  @"^/jobs/[0-9]+(?:-[a-z0-9-]+)?/?$",
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);

 public static bool IsApprovedProviderUrl(string source,string? raw)
 {
  if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri) ||
     uri.Scheme!=Uri.UriSchemeHttps ||
     uri.UserInfo.Length!=0 || uri.Fragment.Length!=0 ||
     uri.Query.Length!=0 || uri.Port!=443)return false;
  return source switch
  {
   "dou"=>uri.Host.Equals("jobs.dou.ua",StringComparison.OrdinalIgnoreCase) &&
     DouPath.IsMatch(uri.AbsolutePath),
   "djinni"=>(uri.Host.Equals("djinni.co",StringComparison.OrdinalIgnoreCase) ||
      uri.Host.Equals("www.djinni.co",StringComparison.OrdinalIgnoreCase)) &&
     DjinniPath.IsMatch(uri.AbsolutePath),
   _=>false
  };
 }

 public async Task<StatusOnlyRefreshReport> RunAsync(
  int maxJobs,int minAgeHours,int recentDays,CancellationToken ct)
 {
  if(maxJobs is <1 or >30 ||
     minAgeHours is <1 or >720 ||
     recentDays is <1 or >90)
   throw new ArgumentOutOfRangeException(nameof(maxJobs),
     "Allowed max jobs 1-30; status age 1-720 hours; recent window 1-90 days");

  var started=DateTimeOffset.UtcNow;
  var candidates=await storage.ReadStatusRefreshCandidatesAsync(
   maxJobs,minAgeHours,recentDays,ct);
  var decisions=await storage.ReadEmploymentDecisionsAsync(ct);
  int attempted=0,closed=0,unknown=0,failed=0,skipped=0;
  foreach(var job in candidates)
  {
   ct.ThrowIfCancellationRequested();
   if(!IsApprovedProviderUrl(job.Source,job.Url) ||
     (Storage.IsAcceptableEmploymentUrl(job.Url) &&
     decisions.ContainsKey(Storage.CanonicalEmploymentUrl(job.Url))))
   {
    skipped++;
    continue;
   }
   attempted++;
   try
   {
    var html=await fetcher.GetAsync(job.Url,ct);
    var observation=VacancyStatusEvidence.FromProviderHtml(html);
    // The HTML extractor is intentionally unable to assert true/open.
    // Never interpret a 200 response or Apply link as proof of hiring.
    if(observation.IsOpen==true)
     throw new InvalidDataException("Positive job status is not supported");
    var recorded=await storage.SaveStatusOnlyAsync(
     job,observation,DateTimeOffset.UtcNow,ct);
    if(!recorded)
    {
     skipped++;
    }
    else if(observation.IsOpen==false)
    {
     closed++;
    }
    else unknown++;
   }
   catch(Exception error) when(!ct.IsCancellationRequested)
   {
    failed++;
    // Do not overwrite the last valid status with a failed HTTP request.
    await storage.SaveError(job.Source,job.Url,error.GetType().Name,ct);
   }
   progress?.Invoke(
    $"Status refresh: attempted={attempted}, closed={closed}, unverified={unknown}, failed={failed}");
  }
  return new StatusOnlyRefreshReport(started,DateTimeOffset.UtcNow,
   candidates.Count,attempted,closed,unknown,failed,skipped,
   "DOU/Djinni only. Status from explicit provider HTML only; no positive-open assertions.");
 }
}
