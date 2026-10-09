using System.Text.Json;

namespace JobRadar;

public sealed record PendingVacancy(JobRef Job, int PreviousAttempts);
public sealed record PendingCounts(int Total, int Ready, int Deferred, int Blocked, int Exhausted);
public sealed record PendingRetryReport(DateTimeOffset StartedAt, DateTimeOffset EndedAt,
  int Attempted, int Recovered, int Failed, PendingCounts Before, PendingCounts After);

/// <summary>
/// Replays a bounded backlog of failed/missing full-text jobs from PostgreSQL.
/// This is independent from RSS/search freshness: disappearing from a feed does
/// not erase a previously discovered vacancy.
/// </summary>
public sealed class PendingRetry(HttpFetcher fetcher, Storage storage, RadarOptions options)
{
 public string[] EnabledSources()
 {
  var sources=new List<string>();
  if(options.EnabledRobota)sources.Add("robota");
  if(options.EnabledDou)sources.Add("dou");
  if(options.EnabledDjinni)sources.Add("djinni");
  if(options.EnabledWorkUa)sources.Add("workua");
  return sources.ToArray();
 }

 public async Task<PendingRetryReport> RunAsync(CancellationToken ct)
 {
  var started=DateTimeOffset.UtcNow;
  var sources=EnabledSources();
  int maxAttempts=Math.Clamp(options.PendingMaxAttempts,1,20);
  int ageMinutes=Math.Clamp(options.PendingMinimumAgeMinutes,0,10080);
  int limit=Math.Clamp(options.PendingBatchSize,1,100);

  var before=await storage.GetPendingCountsAsync(sources,maxAttempts,ageMinutes,ct);
  var pending=await storage.GetPendingVacanciesAsync(sources,maxAttempts,ageMinutes,limit,ct);
  int attempted=0,recovered=0,failed=0;
  var robota=new RobotaCompanyDetails(fetcher);
  foreach(var item in pending)
  {
   ct.ThrowIfCancellationRequested();
   attempted++;
   try
   {
    var job=item.Job;
    JobDetail detail;
    if(job.Source=="robota")
    {
     var info=await robota.GetAsync(job,ct);
     detail=new JobDetail(job with {PublishedAt=job.PublishedAt??info.PublishedAt},
       info.Description,null,DateTimeOffset.UtcNow);
    }
    else
    {
     var html=await fetcher.GetAsync(job.Url,ct);
     var description=Parsers.Description(job.Source,html);
     detail=new JobDetail(job,description,Parsers.OpenStatus(html),DateTimeOffset.UtcNow);
    }
    if(detail.Description.Length<80)
     throw new InvalidDataException("Full description unavailable or too short");
    await storage.Save(detail,ct);
    recovered++;
   }
   catch(Exception e) when(!ct.IsCancellationRequested)
   {
    failed++;
    await storage.SaveError(item.Job.Source,item.Job.Url,e.Message,ct);
   }
  }

  var after=await storage.GetPendingCountsAsync(sources,maxAttempts,ageMinutes,ct);
  var report=new PendingRetryReport(started,DateTimeOffset.UtcNow,attempted,recovered,failed,before,after);
  await storage.SavePendingRetryReportAsync(report,ct);
  return report;
 }

 public async Task<PendingCounts> StatusAsync(CancellationToken ct)
  => await storage.GetPendingCountsAsync(EnabledSources(),
      Math.Clamp(options.PendingMaxAttempts,1,20),
      Math.Clamp(options.PendingMinimumAgeMinutes,0,10080),ct);

 public static string Render<T>(T value)
  => JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true});
}
