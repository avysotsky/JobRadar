namespace JobRadar;

/// <summary>
/// Crawls configured queries with explicit completeness metrics. A reconciled
/// query is not a claim of whole-site coverage.
/// </summary>
public sealed class Crawler(HttpFetcher fetcher,Storage storage,RadarOptions options)
{
 public async Task<CrawlReport> RunAsync(IEnumerable<IJobSource> sources,CancellationToken ct)
 {
  var started=DateTimeOffset.UtcNow;
  var summaries=new List<SourceResult>();
  var robotaDetails=new RobotaCompanyDetails(fetcher);
  var previousCounts=await storage.GetPreviousReferenceCountsAsync(ct);
  foreach(var source in sources)
  {
   int pages=0,references=0,details=0,failedDetails=0,rawRecords=0,droppedRecords=0;
   int? total=null;
   string? error=null;
   bool uncertain=source.IsSinglePageFeed;
   bool reconciled=false;
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   int maxPages=source.IsSinglePageFeed?1:Math.Max(1,options.MaxPagesPerSource);
   for(int page=0;page<maxPages;page++)
   {
    string url;
    try{url=source.ListingUrl(page);}
    catch(NotSupportedException e){uncertain=true;error=e.Message;break;}
    string payload;
    try{payload=await fetcher.GetAsync(url,ct);}
    catch(Exception e) when(!ct.IsCancellationRequested)
    {
     await storage.SaveError(source.Name,url,e.Message,ct);
     error=e.Message;uncertain=true;break;
    }
    pages++;
    IReadOnlyList<JobRef> listings;
    try
    {
     listings=source.ParseListings(payload);
     if(source is RobotaApiSource robotaSource)
     {
      var raw=robotaSource.RawDocumentCount(payload);
      rawRecords+=raw;
      var dropped=Math.Max(0,raw-listings.Count);
      droppedRecords+=dropped;
      if(dropped>0)
      {
       uncertain=true;
       error??="Robota API contained records with missing or invalid vacancy identifiers";
      }
     }
     if(!source.IsSinglePageFeed)total??=source.ReportedTotal(payload);
    }
    catch(Exception e) when(!ct.IsCancellationRequested)
    {
     await storage.SaveError(source.Name,url,"Parse failure: "+e.Message,ct);
     error=e.Message;uncertain=true;break;
    }
    // Only accept empty results as legitimate when the source actually reported zero.
    if(listings.Count==0)
    {
     if(page==0 && total==0){reconciled=true;break;}
     uncertain=true;error="Unexpected empty vacancy identifiers from nonempty or unverified search";break;
    }
    var fresh=listings.Where(x=>seen.Add(x.Url)).ToList();
    if(fresh.Count==0)
    {
     if(total.HasValue && references>=total.Value)reconciled=true;
     else uncertain=true; // identical page could be a paging bug
     break;
    }
    foreach(var job in fresh)
    {
     references++;
     // Persist every discovered vacancy and the query which discovered it.
     await storage.SaveDiscovered(job,ct,source.Name);
     try
     {
      if(source is RobotaApiSource)
      {
       var detail=await robotaDetails.GetAsync(job,ct);
       if(detail.Description.Length<80)
          throw new InvalidDataException("Robota detail missing or too short");
       var enriched=job with { PublishedAt=job.PublishedAt??detail.PublishedAt };
       await storage.Save(new JobDetail(enriched,detail.Description,null,DateTimeOffset.UtcNow),ct);
      }
      else
      {
       var detailHtml=await fetcher.GetAsync(job.Url,ct);
       var description=Parsers.Description(source.Name,detailHtml);
       if(description.Length<80)
          throw new InvalidDataException("Vacancy detail missing or too short");
       await storage.Save(new JobDetail(job,description,Parsers.OpenStatus(detailHtml),DateTimeOffset.UtcNow),ct);
      }
      details++;
     }
     catch(Exception e) when(!ct.IsCancellationRequested)
     {
      failedDetails++;
      await storage.SaveError(job.Source,job.Url,e.Message,ct);
     }
    }
    if(source.IsSinglePageFeed)break;
    if(total.HasValue && references>=total.Value)
    {
     reconciled=references==total.Value;
     if(references>total.Value)
     {
      uncertain=true;
      error="Source returned more unique IDs than its advertised total";
     }
     break;
    }
   }
   if(!source.IsSinglePageFeed && pages>=maxPages && !reconciled)
     uncertain=true;
   if(total.HasValue && references!=total.Value)
     uncertain=true;
   if(!source.IsSinglePageFeed && !total.HasValue)
     uncertain=true; // enumeration without an independent total is unverified
   previousCounts.TryGetValue(source.Name,out var previousRefs);
   var warning=CoverageDrift.Warning(previousRefs, references);
   if(warning!=null)uncertain=true;
   string status=pages==0?"FAILED":
     failedDetails>0||uncertain||error!=null?"PARTIAL":
     reconciled?"QUERY_RECONCILED":"UNVERIFIED_COVERAGE";
   summaries.Add(new SourceResult(source.Name,pages,references,details,failedDetails,total,status,error,
     previousRefs>0?previousRefs:null,warning,
     source is RobotaApiSource?rawRecords:null,source is RobotaApiSource?droppedRecords:null));
  }
  var report=new CrawlReport(started,DateTimeOffset.UtcNow,summaries);
  await storage.SaveReport(report,ct);
  return report;
 }
}
