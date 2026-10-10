using JobRadar;
using System.Text.Json;
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
var cs=Environment.GetEnvironmentVariable("JOBRADAR_DB") ?? options.ConnectionString;
var storage=new Storage(cs);
try{await storage.Initialize(CancellationToken.None);}catch(Exception e){Console.Error.WriteLine("Database unavailable: "+e.Message);return 3;}
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