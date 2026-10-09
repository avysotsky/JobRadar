using JobRadar;
using System.Text.Json;
var configPath=Path.Combine(AppContext.BaseDirectory,"appsettings.json");
if(!File.Exists(configPath)){Console.Error.WriteLine($"Missing configuration: {configPath}");return 2;}
var options=JsonSerializer.Deserialize<RadarOptions>(await File.ReadAllTextAsync(configPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? new RadarOptions();
var cs=Environment.GetEnvironmentVariable("JOBRADAR_DB") ?? options.ConnectionString;
var storage=new Storage(cs);
try{await storage.Initialize(CancellationToken.None);}catch(Exception e){Console.Error.WriteLine("Database unavailable: "+e.Message);return 3;}
using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds)};
http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
var sources=new List<IJobSource>();if(options.EnabledDou)sources.Add(new DouSource());if(options.EnabledDjinni)sources.Add(new DjinniSource());
var crawler=new Crawler(new HttpFetcher(http,options.DelayMilliseconds),storage,options);
var once=args.Contains("--once");
async Task Run(CancellationToken ct)
{
 try
 {
  var report=await crawler.RunAsync(sources,ct);
  Directory.CreateDirectory(options.OutputDirectory);
  var file=Path.Combine(options.OutputDirectory,$"crawl-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
  var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
  await File.WriteAllTextAsync(file,json,ct);Console.WriteLine(json);
 }
 catch(Exception e) when(e is not OperationCanceledException){Console.Error.WriteLine("CRAWL FAILED: "+e);}
}
if(once){await Run(CancellationToken.None);return 0;}
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
 if(!cts.IsCancellationRequested)await Run(cts.Token);
}
return 0;