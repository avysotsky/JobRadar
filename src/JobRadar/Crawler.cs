namespace JobRadar;

/// <summary>
/// Crawls configured queries with explicit completeness metrics. A reconciled
/// query is not a claim of whole-site coverage.
/// </summary>
public sealed class Crawler(HttpFetcher fetcher,Storage storage,RadarOptions options,
 Action<string>? progress=null)
{
 public async Task<CrawlReport> RunAsync(IEnumerable<IJobSource> sources,CancellationToken ct)
 {
  var started=DateTimeOffset.UtcNow;
  var summaries=new List<SourceResult>();
  var robotaDetails=new RobotaCompanyDetails(fetcher);
  var previousCounts=await storage.GetPreviousReferenceCountsAsync(ct);
  var sourceList=sources.ToArray();
  for(int sourceIndex=0;sourceIndex<sourceList.Length;sourceIndex++)
  {
   var source=sourceList[sourceIndex];
   var label=$"Source {sourceIndex+1}/{sourceList.Length} ({source.Name})";
   int pages=0,references=0,details=0,failedDetails=0,rawRecords=0,droppedRecords=0,cachedDetails=0;
   int? total=null;
   string? error=null;
   bool uncertain=source.IsSinglePageFeed;
   // A 403 on Robota company details is not evidence the job was removed.
   // Continue index discovery, but never request a disabled full-text endpoint.
   if(source is RobotaApiSource && !options.EnabledRobotaDetails)
   {
    uncertain=true;
    error="Robota full-text disabled after observed HTTP 403; only listing/preview coverage available";
   }
   bool reconciled=false, overlapReported=false, totalDriftReported=false;
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   int maxPages=source.IsSinglePageFeed?1:Math.Max(1,options.MaxPagesPerSource);
   progress?.Invoke($"{label}: START, max pages={maxPages}");
   for(int page=0;page<maxPages;page++)
   {
    string url;
    try{url=source.ListingUrl(page);}
    catch(NotSupportedException e){uncertain=true;error=e.Message;break;}
    progress?.Invoke($"{label}: fetching page {page+1}/{maxPages}");
    string payload;
    try{payload=await fetcher.GetAsync(url,ct);}
    catch(Exception e) when(!ct.IsCancellationRequested)
    {
     await storage.SaveError(source.Name,url,e.Message,ct);
     error=source is RobotaApiSource
      ?RobotaCoverageSignals.FetchFailed+": "+e.Message:e.Message;
     uncertain=true;break;
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
     if(!source.IsSinglePageFeed)
     {
      var pageTotal=source.ReportedTotal(payload);
      if(page==0)total=pageTotal;
      else if(source is RobotaApiSource && pageTotal!=total)
      {
       // Query results are not a stable snapshot when the API's total changes
       // (or disappears) between pages. Retain the first-page total as the
       // reconciliation baseline, but never advertise a complete query.
       uncertain=true;
       if(!totalDriftReported)
       {
        totalDriftReported=true;
        var message=pageTotal is null
         ?"Robota API reported total missing on subsequent page"
         :total is null
          ?"Robota API reported total first appeared on subsequent page"
          :"Robota API reported total changed between pages";
        var drift=RobotaCoverageSignals.TotalDrift+": "+message;
        error=string.IsNullOrWhiteSpace(error)?drift:error+"; "+drift;
       }
      }
      else if(source is not RobotaApiSource)total??=pageTotal;
     }
    }
    catch(Exception e) when(!ct.IsCancellationRequested)
    {
     await storage.SaveError(source.Name,url,"Parse failure: "+e.Message,ct);
     error=source is RobotaApiSource
      ?RobotaCoverageSignals.ParseFailed+": "+e.Message:e.Message;
     uncertain=true;break;
    }
    progress?.Invoke($"{label}: page {page+1} returned {listings.Count} listings; provider total={total?.ToString()??"unknown"}");
    // Only accept empty results as legitimate when the source actually reported zero.
    if(listings.Count==0)
    {
     // A true zero-result search needs both an explicit zero total and an
     // empty provider array. Invalid raw records are not evidence of zero.
     if(page==0 && total==0 &&
        (source is not RobotaApiSource || rawRecords==0))
     {
      reconciled=true;
      // No vacancy requires full-text retrieval. A provider-confirmed zero
      // cannot fail solely because Robota detail fetching is paused.
      if(source is RobotaApiSource && !options.EnabledRobotaDetails)
      {
       uncertain=false;
       error=null;
      }
      break;
     }
     uncertain=true;
     var emptyMessage="Unexpected empty vacancy identifiers from nonempty or unverified search";
     if(source is RobotaApiSource)
     {
      var signal=page==0?RobotaCoverageSignals.EmptyUnverified:
       RobotaCoverageSignals.EmptyBeforeTotal;
      emptyMessage=signal+": "+emptyMessage;
     }
     error=string.IsNullOrWhiteSpace(error)?emptyMessage:error+"; "+emptyMessage;
     break;
    }
    var fresh=listings.Where(x=>seen.Add(x.Url)).ToList();
    // A stable advertised total is insufficient when a later search page
    // repeats an earlier vacancy: shifting pages can conceal unseen IDs.
    // Preserve each fresh discovery, but never certify Robota query coverage.
    if(source is RobotaApiSource && page>0 && fresh.Count<listings.Count)
    {
     uncertain=true;
     if(!overlapReported)
     {
      overlapReported=true;
      // Keep prior diagnostics (e.g. intentionally paused HTTP 403 details).
      const string overlap="Robota API search pages overlap: repeated vacancy IDs across pages";
      error=string.IsNullOrWhiteSpace(error)?overlap:error+"; "+overlap;
     }
    }
    if(fresh.Count==0)
    {
     if(source is not RobotaApiSource && total.HasValue && references>=total.Value)
      reconciled=true;
     else uncertain=true; // repeated page is not proof of complete pagination
     break;
    }
    foreach(var job in fresh)
    {
     references++;
     // Persist every discovered vacancy and the query which discovered it.
     await storage.SaveDiscovered(job,ct,source.Name);
     try
     {
      if(await storage.HasRecentFullTextAsync(job,Math.Clamp(options.DetailRefreshHours,1,720),ct))
      {
       cachedDetails++;
       continue;
      }
      if(source is RobotaApiSource)
      {
       if(!options.EnabledRobotaDetails)
        continue; // preserve the indexed vacancy/preview without calling HTTP 403 endpoint
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
       var observed=VacancyStatusEvidence.FromProviderHtml(detailHtml);
       await storage.Save(new JobDetail(job,description,observed.IsOpen,DateTimeOffset.UtcNow,
         observed.EvidenceCode),ct);
      }
      details++;
     }
     catch(Exception e) when(!ct.IsCancellationRequested)
     {
      failedDetails++;
      await storage.SaveError(job.Source,job.Url,e.Message,ct);
     }
     finally
     {
      // Keep console output bounded while showing that details are advancing.
      if(references==1 || references%5==0)
       progress?.Invoke($"{label}: processed={references}, full={details}, cached={cachedDetails}, failed={failedDetails}");
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
     source is RobotaApiSource?rawRecords:null,source is RobotaApiSource?droppedRecords:null,cachedDetails));
   progress?.Invoke($"{label}: FINISHED status={status}, pages={pages}, found={references}, full={details}, cached={cachedDetails}, failed={failedDetails}"+
     (source is RobotaApiSource && !options.EnabledRobotaDetails?", Robota full text paused (HTTP 403)":""));
  }
  var report=new CrawlReport(started,DateTimeOffset.UtcNow,summaries);
  await storage.SaveReport(report,ct);
  return report;
 }
}
