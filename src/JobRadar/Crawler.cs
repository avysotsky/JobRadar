namespace JobRadar;
public sealed class Crawler(HttpFetcher fetcher,Storage storage,RadarOptions options)
{
 public async Task<CrawlReport> RunAsync(IEnumerable<IJobSource> sources,CancellationToken ct)
 {
  var started=DateTimeOffset.UtcNow;
  var summaries=new List<SourceResult>();
  var robotaDetails=new RobotaCompanyDetails(fetcher);
  foreach(var source in sources)
  {
   var pages=0;var refs=0;var success=0;var fail=0;int? total=null;
   string? error=null;bool uncertain=source.IsSinglePageFeed;
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   var maxPages=source.IsSinglePageFeed?1:options.MaxPagesPerSource;
   for(int p=0;p<maxPages;p++)
   {
    string url;
    try{url=source.ListingUrl(p);}
    catch(Exception e) when(e is NotSupportedException)
    {uncertain=true;error=e.Message;break;}
    string payload;
    try{payload=await fetcher.GetAsync(url,ct);}
    catch(Exception e) when(!ct.IsCancellationRequested)
    {await storage.SaveError(source.Name,url,e.Message,ct);error=e.Message;uncertain=true;break;}
    pages++;
    IReadOnlyList<JobRef> listings;
    try
    {
     listings=source.ParseListings(payload);
     if(!source.IsSinglePageFeed)total??=source.ReportedTotal(payload);
    }
    catch(Exception e) when(!ct.IsCancellationRequested)
    {await storage.SaveError(source.Name,url,"Parse failure: "+e.Message,ct);error=e.Message;uncertain=true;break;}
    if(listings.Count==0)
    {uncertain=true;error="No vacancy identifiers extracted; potentially empty or broken source";break;}
    var fresh=listings.Where(x=>seen.Add(x.Url)).ToList();
    if(fresh.Count==0)break;
    foreach(var job in fresh)
    {
     refs++;
     // Save identifiers and previews before detail retrieval so failures cannot erase discovery.
     await storage.SaveDiscovered(job,ct);
     try
     {
      if(source is RobotaApiSource)
      {
       var detail=await robotaDetails.GetAsync(job,ct);
       if(detail.Description.Length<80)throw new InvalidDataException("Robota API detail missing or too short");
       var enriched=job with { PublishedAt=job.PublishedAt??detail.PublishedAt };
       await storage.Save(new JobDetail(enriched,detail.Description,null,DateTimeOffset.UtcNow),ct);
      }
      else
      {
       var detailHtml=await fetcher.GetAsync(job.Url,ct);
       var description=Parsers.Description(source.Name,detailHtml);
       if(description.Length<80)throw new InvalidDataException("Vacancy detail missing or too short");
       await storage.Save(new JobDetail(job,description,Parsers.OpenStatus(detailHtml),DateTimeOffset.UtcNow),ct);
      }
      success++;
     }
     catch(Exception e) when(!ct.IsCancellationRequested)
     {fail++;await storage.SaveError(source.Name,job.Url,e.Message,ct);}
    }
    if(source.IsSinglePageFeed || (total.HasValue && refs>=total.Value))break;
    // Page exhaustion must be evidenced by empty/duplicate page or a verified count.
    // A short page alone is NOT proof of exhaustion.
   }
   if(!source.IsSinglePageFeed && pages>=maxPages)uncertain=true;
   if(total.HasValue && refs<total.Value)uncertain=true;
   // Detail parse failures cannot be silently treated as a successful source scan.
   var status=pages==0?"FAILED":fail>0||uncertain||error!=null?"PARTIAL":"UNVERIFIED_COVERAGE";
   summaries.Add(new SourceResult(source.Name,pages,refs,success,fail,total,status,error));
  }
  var report=new CrawlReport(started,DateTimeOffset.UtcNow,summaries);
  await storage.SaveReport(report,ct);
  return report;
 }
}