using JobRadar;
using System.Text.Json;
// Explicit UTF-8 for redirected stdout/stderr on Windows, independent of
// legacy OEM code pages. For reproducible exports prefer direct-file mode.
Console.OutputEncoding=new System.Text.UTF8Encoding(false,true);
var configPath=Path.Combine(AppContext.BaseDirectory,"appsettings.json");
if(!File.Exists(configPath)){Console.Error.WriteLine($"Missing configuration: {configPath}");return 2;}
var options=JsonSerializer.Deserialize<RadarOptions>(await File.ReadAllTextAsync(configPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? new RadarOptions();
// Flush each progress event so a long-running --once scan is observable in PowerShell.
void ShowProgress(string message)
{
 Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
 Console.Out.Flush();
}
if(args.Contains("--smoke-feed-variants"))return await FeedVariantSmoke.RunAsync(options,CancellationToken.None);
if(args.Contains("--smoke-robota-pages"))return await RobotaPagingSmoke.RunAsync(options,CancellationToken.None);
if(args.Contains("--smoke-robota"))return await RobotaLiveSmoke.RunAsync(options,CancellationToken.None);
if(args.Contains("--smoke-djinni"))return await PublicSourceSmoke.RunAsync(new DjinniSource(),options,CancellationToken.None);
if(args.Contains("--smoke-dou"))return await PublicSourceSmoke.RunAsync(new DouSource(),options,CancellationToken.None);
if(args.Contains("--smoke-workua"))return await PublicSourceSmoke.RunAsync(new WorkUaSource("c#"),options,CancellationToken.None);
var remoteReviewArg=args.FirstOrDefault(x=>
 x.StartsWith("--remote-boards-review=",StringComparison.OrdinalIgnoreCase));
if(remoteReviewArg is not null)
{
 try
 {
  var path=remoteReviewArg["--remote-boards-review=".Length..];
  var summary=await RemoteReviewOffline.RunAsync(path,Console.Out,CancellationToken.None);
  Console.Error.WriteLine("Remote review priorities: "+JsonlOutput.Serialize(summary));
  return 0;
 }
 catch(Exception e) when(e is not OperationCanceledException)
 {
  Console.Error.WriteLine("Remote offline review failed: "+e.Message);
  return 4;
 }
}
var remoteAuditArg=args.FirstOrDefault(x=>
 x.StartsWith("--remote-boards-audit=",StringComparison.OrdinalIgnoreCase));
if(remoteAuditArg is not null)
{
 try
 {
  var path=remoteAuditArg["--remote-boards-audit=".Length..];
  var audit=await RemoteJsonlIntegrity.CheckAsync(path,CancellationToken.None);
  Console.WriteLine(JsonlOutput.Serialize(audit));
  return audit.Valid?0:4;
 }
 catch(Exception e) when(e is not OperationCanceledException)
 {
  Console.Error.WriteLine("Remote JSONL audit failed: "+e.Message);
  return 4;
 }
}
// A canonical WWR JobRef bridge exists, but DB retention is deliberately
// prohibited pending source-specific written permission and a deletion policy.
// Neither EnabledWwr nor this command ever initiates database writes.
if(args.Contains("--wwr-ingest-once"))
{
 Console.Error.WriteLine("WWR DB ingestion BLOCKED: source retention/use permission not verified. Use --wwr-once for read-only RSS.");
 return 4;
}
if(args.Contains("--remote-boards-once") || args.Contains("--wwr-once"))
{
 // Manual-only public RSS/API discovery. Neither mode touches PostgreSQL.
 // --wwr-once makes exactly two WWR requests (third is explicit opt-in).
 try
 {
  bool wwrOnly=args.Contains("--wwr-once");
  if(wwrOnly && args.Contains("--remote-boards-once"))
   throw new ArgumentException("Select --wwr-once OR --remote-boards-once");
  bool fullstack=args.Contains("--wwr-fullstack");
  if(fullstack && !wwrOnly)
   throw new ArgumentException("--wwr-fullstack requires --wwr-once");
  using var handler=new HttpClientHandler {AllowAutoRedirect=false};
  using var remoteClient=new HttpClient(handler)
   {Timeout=TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds,5,60))};
  remoteClient.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
  var outputPrefix=wwrOnly?"--wwr-output=":"--remote-boards-output=";
  var outputArg=args.FirstOrDefault(x=>
   x.StartsWith(outputPrefix,StringComparison.OrdinalIgnoreCase));
  RemoteBoardsReport report;
  if(outputArg is null)
  {
   report=wwrOnly
    ?await RemoteBoards.ScanWwrAsync(remoteClient,Console.Out,
      message=>Console.Error.WriteLine(message),CancellationToken.None,fullstack)
    :await RemoteBoards.ScanAsync(remoteClient,Console.Out,
      message=>Console.Error.WriteLine(message),CancellationToken.None);
  }
  else
  {
   var path=outputArg[outputPrefix.Length..];
   var result=await RemoteBoardFileExport.WriteAsync(remoteClient,path,
    message=>Console.Error.WriteLine(message),CancellationToken.None,
    wwrOnly,fullstack);
   report=result.Report;
   Console.Error.WriteLine("Verified UTF-8 JSONL: "+JsonlOutput.Serialize(
    new {result.Path,result.Integrity}));
  }
  Console.Error.WriteLine((wwrOnly?"WWR RSS":"Remote boards")+
   " completed: "+JsonlOutput.Serialize(report));
  return report.Sources.Any(x=>x.Status=="FAILED"||x.DroppedRecords>0)?4:0;
 }
 catch(Exception e) when(e is not OperationCanceledException)
 {
  Console.Error.WriteLine("Remote boards scan/export failed: "+e.Message);
  return 4;
 }
}
if(args.Contains("--freelancehunt-once"))
{
 // No PostgreSQL, no scheduled scans, no bids or writes to provider.
 try
 {
  using var handler=new HttpClientHandler {AllowAutoRedirect=false};
  using var client=new HttpClient(handler)
   {Timeout=TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds,5,60))};
  client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
  var report=await FreelancehuntProjects.ScanAsync(client,Console.Out,
   Environment.GetEnvironmentVariable("FREELANCEHUNT_API_TOKEN"),
   options.FreelancehuntSkillIds,options.FreelancehuntMaxPages,
   message=>Console.Error.WriteLine(message),CancellationToken.None);
  Console.Error.WriteLine("Freelancehunt completed: "+JsonlOutput.Serialize(report));
  return report.DroppedRecords==0?0:4;
 }
 catch(Exception e) when(e is not OperationCanceledException)
 {
  Console.Error.WriteLine("Freelancehunt scan unavailable: "+e.Message);
  return 4;
 }
}
if(args.Contains("--freelancer-once"))
{
 // Opt-in command is deliberately independent of PostgreSQL and the daily
 // employment crawler. Writes transient JSONL to stdout only.
 try
 {
  // OAuth is carried in a custom header; redirects to third-party hosts
  // must never forward that header.
  using var freelancerHandler=new HttpClientHandler{AllowAutoRedirect=false};
  using var freelancerHttp=new HttpClient(freelancerHandler)
   {Timeout=TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds,5,60))};
  freelancerHttp.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
  var report=await FreelancerProjects.ScanAsync(
   freelancerHttp,Console.Out,
   Environment.GetEnvironmentVariable("FLN_OAUTH_TOKEN"),
   Environment.GetEnvironmentVariable("FLN_AUTOMATION_PERMISSION_GRANTED"),
   options.FreelancerQueries,options.FreelancerPageSize,options.FreelancerMaxPagesPerQuery,
   message=>Console.Error.WriteLine(message),CancellationToken.None);
  Console.Error.WriteLine("Freelancer completed: "+JsonlOutput.Serialize(report));
  return 0;
 }
 catch(Exception e) when(e is not OperationCanceledException)
 {
  Console.Error.WriteLine("Freelancer scan unavailable: "+e.Message);
  return 4;
 }
}
var cs=Environment.GetEnvironmentVariable("JOBRADAR_DB") ?? options.ConnectionString;
var storage=new Storage(cs);
try{await storage.Initialize(CancellationToken.None);}catch(Exception e){Console.Error.WriteLine("Database unavailable: "+e.Message);return 3;}
if(args.Any(x=>x.StartsWith("--project-track-",StringComparison.OrdinalIgnoreCase)))
 return await ProjectTrackerCli.RunAsync(storage,args,Console.Out,Console.Error,CancellationToken.None);
if(args.Contains("--opportunities-preview"))
{
 // Reconcile stored employment with operator-selected local, previously
 // authorized JSONL snapshots. This command never contacts external boards
 // and never persists third-party inputs back to PostgreSQL.
 static string? OptionalPath(string[] argv,string name)
 {
  var prefix=name+"=";
  var value=argv.FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
  return value is null?null:value[prefix.Length..];
 }
 var paths=new (OpportunityInputKind Kind,string? Path)[]
 {
  (OpportunityInputKind.Freelancehunt,OptionalPath(args,"--freelancehunt-file")),
  (OpportunityInputKind.Freelancer,OptionalPath(args,"--freelancer-file")),
  (OpportunityInputKind.RemoteBoards,OptionalPath(args,"--remote-boards-file"))
 };
 var readers=new Dictionary<OpportunityInputKind,TextReader>();
 try
 {
  foreach(var (kind,path) in paths)
  {
   if(path is null)continue;
   if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
    throw new FileNotFoundException("Local JSONL snapshot not found: "+kind);
   // Fail closed on damaged UTF-8 rather than importing silently
   // substituted Unicode replacement characters.
   if(kind==OpportunityInputKind.RemoteBoards)
   {
    var integrity=await RemoteJsonlIntegrity.CheckAsync(path,CancellationToken.None);
    if(!integrity.Valid)
     throw new InvalidDataException("Remote JSONL failed strict UTF-8/JSON integrity: "+
      JsonlOutput.Serialize(integrity));
   }
   readers.Add(kind,new StreamReader(path,new System.Text.UTF8Encoding(false,true),
    detectEncodingFromByteOrderMarks:true));
  }
  var jobs=await storage.ReadVacanciesForTriageAsync(
   Math.Clamp(options.ExportMaxRecords,1,50000),CancellationToken.None);
  long stored=await storage.CountVacanciesForTriageAsync(CancellationToken.None);
  var histories=await storage.ReadDiscoveryHistoriesAsync(CancellationToken.None);
  var coverage=await storage.ReadLatestCoverageAsync(20,CancellationToken.None);
  var tracked=await storage.ReadTrackedProjectsAsync(CancellationToken.None);
  var result=await OpportunityPreview.WriteAsync(
   jobs,histories,coverage,readers,Console.Out,CancellationToken.None,tracked);
  Console.Error.WriteLine("Opportunities preview: "+JsonlOutput.Serialize(new
   {Result=result,TotalStoredEmployment=stored,EmploymentOmittedByLimit=Math.Max(0L,stored-jobs.Count)}));
  // Exit nonzero when export is truncated or any imported records failed to
  // parse. Import errors are visible in per-source coverage metrics.
  return result.Inputs.Any(x=>x.Rejected>0)||stored>jobs.Count?4:0;
 }
 catch(Exception e) when(e is not OperationCanceledException)
 {
  Console.Error.WriteLine("Opportunities preview failed: "+e.Message);
  return 4;
 }
 finally
 {
  foreach(var reader in readers.Values)reader.Dispose();
 }
}
if(args.Contains("--export-jsonl"))
{
 var output=Path.GetFullPath(options.OutputDirectory);
 Directory.CreateDirectory(output);
 var path=Path.Combine(output,$"jobs-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.jsonl");
 await using var writer=new StreamWriter(path,false,new System.Text.UTF8Encoding(false));
 long totalStored=await storage.CountVacanciesForTriageAsync(CancellationToken.None);
 int count=await storage.ExportJobsJsonlAsync(writer,Math.Clamp(options.ExportMaxRecords,1,50000),CancellationToken.None);
 await writer.FlushAsync();
 long omitted=Math.Max(0L,totalStored-count);
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
  Path=path,Rows=count,MaxRecords=options.ExportMaxRecords,
  TotalStored=totalStored,OmittedByLimit=omitted
 }));
 if(omitted>0)Console.Error.WriteLine(
  "Export truncated by ExportMaxRecords; "+omitted+" stored vacancies were not exported.");
 return 0;
}
if(args.Contains("--triage-jsonl"))
{
 var summary=await TriageExport.WriteAsync(storage,Path.GetFullPath(options.OutputDirectory),
   Math.Clamp(options.ExportMaxRecords,1,50000),CancellationToken.None);
 Console.WriteLine(JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
 return 0;
}
if(args.Contains("--pending-status"))
{
 using var statusClient=new HttpClient();
 var retry=new PendingRetry(new HttpFetcher(statusClient,0),storage,options);
 Console.WriteLine(PendingRetry.Render(await retry.StatusAsync(CancellationToken.None)));
 return 0;
}
if(args.Contains("--retry-failed"))
{
 using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds)};
 client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
 var retry=new PendingRetry(new HttpFetcher(client,options.DelayMilliseconds),storage,options,ShowProgress);
 var result=await retry.RunAsync(CancellationToken.None);
 Console.WriteLine(PendingRetry.Render(result));
 return result.Failed==0?0:4;
}
if(args.Contains("--jooble-once"))
 return await JoobleRunner.RunAsync(storage,options,CancellationToken.None);
using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds)};
http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
var sources=FeedVariants.Create(options).ToList();
if(options.EnabledRobota)foreach(var term in options.RobotaQueries.Distinct(StringComparer.OrdinalIgnoreCase))sources.Add(new RobotaApiSource(term));
if(options.EnabledWorkUa)foreach(var term in options.WorkUaQueries.Distinct(StringComparer.OrdinalIgnoreCase))sources.Add(new WorkUaSource(term));
var crawler=new Crawler(new HttpFetcher(http,options.DelayMilliseconds),storage,options,ShowProgress);
var once=args.Contains("--once");
async Task<int> Run(CancellationToken ct)
{
 try
 {
  ShowProgress($"Crawl START, configured queries={sources.Count}");
  var report=await crawler.RunAsync(sources,ct);
  Directory.CreateDirectory(options.OutputDirectory);
  var file=Path.Combine(options.OutputDirectory,$"crawl-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
  var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
  await File.WriteAllTextAsync(file,json,ct);
  Console.WriteLine(json);
  var failures=report.Sources.Any(s=>
    s.Status=="FAILED" || s.Error!=null || s.DetailsFailed>0 || s.CoverageWarning!=null);
  if(options.RetryAfterCrawl)
  {
   var retryReport=await new PendingRetry(new HttpFetcher(http,options.DelayMilliseconds),storage,options,ShowProgress).RunAsync(ct);
   Console.WriteLine("Pending details: "+PendingRetry.Render(retryReport));
   if(retryReport.Failed>0)failures=true;
  }
  ShowProgress($"Crawl FINISHED, sources={report.Sources.Count}, found={report.Sources.Sum(s=>s.ReferencesFound)}, full={report.Sources.Sum(s=>s.DetailsFetched)}, failed={report.Sources.Sum(s=>s.DetailsFailed)}");
  if(failures)Console.Error.WriteLine("JobRadar: scan had source errors or unresolved detail failures; consult report.");
  return failures?4:0;
 }
 catch(Exception e) when(e is not OperationCanceledException)
 {
  Console.Error.WriteLine("CRAWL FAILED: "+e);
  return 4;
 }
}
if(once)return await Run(CancellationToken.None);
using var cts=new CancellationTokenSource();Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;cts.Cancel();};
var kyiv=TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
var slots=options.HoursKyiv.Select(TimeOnly.Parse).Order().ToArray();
if(slots.Length==0)throw new ArgumentException("HoursKyiv empty");
Console.WriteLine("JobRadar started. Next scheduled crawl will run at configured Kyiv time.");
while(!cts.IsCancellationRequested)
{
 var local=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,kyiv);
 var next=slots.Select(s=>local.Date.Add(s.ToTimeSpan())).FirstOrDefault(t=>t>local.DateTime);
 if(next==default)next=local.Date.AddDays(1).Add(slots[0].ToTimeSpan());
 var unspecified=DateTime.SpecifyKind(next,DateTimeKind.Unspecified);
 if(kyiv.IsInvalidTime(unspecified))unspecified=unspecified.AddHours(1);
 var offset=kyiv.GetUtcOffset(unspecified);
 var nextUtc=new DateTimeOffset(unspecified,offset).ToUniversalTime();
 var delay=nextUtc-DateTimeOffset.UtcNow;
 if(delay>TimeSpan.Zero)try{await Task.Delay(delay,cts.Token);}catch(OperationCanceledException){break;}
 if(!cts.IsCancellationRequested)
 {
  var code=await Run(cts.Token);
  if(code!=0)Console.Error.WriteLine("JobRadar scan returned exit code "+code);
 }
}
return 0;