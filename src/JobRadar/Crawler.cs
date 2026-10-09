namespace JobRadar;
public sealed class Crawler(HttpFetcher fetcher,Storage storage,RadarOptions options)
{
 public async Task<CrawlReport> RunAsync(IEnumerable<IJobSource> sources,CancellationToken ct)
 {
  var started=DateTimeOffset.UtcNow;var summaries=new List<SourceResult>();
  foreach(var source in sources)
  {
   var pages=0;var refs=0;var success=0;var fail=0;int? total=null;
   string? error=null;bool uncertain=false;var seen=new HashSet<string>();
   for(int p=0;p<options.MaxPagesPerSource;p++)
   {
    string url;
    try{url=source.ListingUrl(p);}catch(NotSupportedException e){uncertain=true;error=e.Message;break;}
    string html;
    try{html=await fetcher.GetAsync(url,ct);}catch(Exception e) when(e is not OperationCanceledException)
    {await storage.SaveError(source.Name,url,e.Message,ct);error=e.Message;uncertain=true;break;}
    pages++;total??=Parsers.ReportedTotal(html);
    var listings=source.ParseListings(html);
    if(listings.Count==0){uncertain=true;error="Zero links returned; source markup may have changed or page may be empty";break;}
    var fresh=listings.Where(x=>seen.Add(x.Url)).ToList();
    if(fresh.Count==0)break;
    foreach(var job in fresh)
    {
     refs++;
     try
     {
      var detailHtml=await fetcher.GetAsync(job.Url,ct);
      var description=Parsers.Description(source.Name,detailHtml);
      if(description.Length<80)throw new InvalidDataException("Detail text missing or too short");
      await storage.Save(new JobDetail(job,description,Parsers.OpenStatus(detailHtml),DateTimeOffset.UtcNow),ct);
      success++;
     }
     catch(Exception e) when(e is not OperationCanceledException)
     {fail++;await storage.SaveError(source.Name,job.Url,e.Message,ct);}
    }
    if(listings.Count<10)break; // heuristic only; coverage remains unverified
   }
   if(pages==options.MaxPagesPerSource)uncertain=true;
   if(total.HasValue && refs<total.Value)uncertain=true;
   var status=pages==0?"FAILED":fail>0||uncertain?"PARTIAL":"UNVERIFIED_COVERAGE";
   summaries.Add(new SourceResult(source.Name,pages,refs,success,fail,total,status,error));
  }
  var report=new CrawlReport(started,DateTimeOffset.UtcNow,summaries);
  await storage.SaveReport(report,ct);return report;
 }
}