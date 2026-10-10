using System.Text.Json;

namespace JobRadar;

/// <summary>
/// Shared presentation model. Employment and freelance ranks are separate
/// domains: their buckets and scores must not be treated as equivalent.
/// No imported third-party descriptions or OAuth tokens are persisted.
/// </summary>
public enum OpportunityKind { Employment, FreelanceProject }
public enum OpportunityInputKind { Freelancehunt, Freelancer, RemoteBoards }

public sealed record UnifiedOpportunity(
 OpportunityKind Kind,string Source,string Url,string Title,string? Organization,
 string Bucket,int Score,string? Currency,decimal? BudgetMinimum,decimal? BudgetMaximum,
 DateTimeOffset? PublishedAt,DateTimeOffset? FirstObservedAt,DateTimeOffset? LastSeenAt,
 long? RecordedDiscoveries,string Attribution,string StatusEvidence,
 IReadOnlyList<string> Reasons,IReadOnlyList<string> Warnings);

public sealed record OpportunityInputCoverage(
 string InputKind,int LinesRead,int Accepted,int Rejected,int Duplicates,
 string CoverageStatus);

public sealed record OpportunityPreviewSummary(
 int Employment,int FreelanceProjects,int TotalUnique,
 int EmploymentLikelyFit,int EmploymentNeedsReview,int EmploymentExcluded,
 int FreelancePriority,int FreelanceReview,int FreelanceLowMatch,
 IReadOnlyList<OpportunityInputCoverage> Inputs,
 IReadOnlyList<StoredSourceCoverage> RecordedCrawlCoverage,
 string HistoryNote,string CoverageNote);

public static class OpportunityPreview
{
 public static async Task<OpportunityPreviewSummary> WriteAsync(
  IReadOnlyList<VacancySnapshot> employment,
  IReadOnlyDictionary<(string Source,string Url),JobDiscoveryHistory> histories,
  IReadOnlyList<StoredSourceCoverage> crawlCoverage,
  IReadOnlyDictionary<OpportunityInputKind,TextReader> inputs,
  TextWriter output,CancellationToken ct)
 {
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var items=new List<UnifiedOpportunity>();
  foreach(var vacancy in employment)
  {
   ct.ThrowIfCancellationRequested();
   if(!Key(OpportunityKind.Employment,vacancy.Url,out var key)||!seen.Add(key!))
    continue; // Existing jobs use (source,url); their URLs normally unique.
   histories.TryGetValue((vacancy.Source,vacancy.Url),out var observation);
   var assessment=VacancyTriage.Assess(vacancy);
   items.Add(new UnifiedOpportunity(
    OpportunityKind.Employment,vacancy.Source,vacancy.Url,vacancy.Title,
    vacancy.Company,assessment.Bucket.ToString(),assessment.Score,
    null,null,null,vacancy.PublishedAt,
    observation?.FirstObservedAt,vacancy.LastSeen,observation?.Observations,
    vacancy.Source,vacancy.StatusEvidence??"NOT_VERIFIED",
    assessment.Reasons,assessment.Warnings));
  }

  var inputCoverage=new List<OpportunityInputCoverage>();
  foreach(var (kind,reader) in inputs.OrderBy(x=>x.Key))
  {
   int total=0,accepted=0,rejected=0,duplicates=0;
   while(true)
   {
    ct.ThrowIfCancellationRequested();
    var line=await reader.ReadLineAsync(ct);
    if(line is null)break;
    if(string.IsNullOrWhiteSpace(line))continue;
    total++;
    // Manual exports only: cap a single untrusted JSONL record. Avoid logging
    // full payloads (they could contain customer details or credentials).
    if(line.Length>128_000){rejected++;continue;}
    if(!TryParse(kind,line,out var item)||
       !Key(item!.Kind,item.Url,out var key))
    {
     rejected++;
     continue;
    }
    if(!seen.Add(key!)){duplicates++;continue;}
    items.Add(item);
    accepted++;
   }
   inputCoverage.Add(new OpportunityInputCoverage(kind.ToString(),total,
    accepted,rejected,duplicates,
    rejected>0?"INPUT_ERRORS":"SNAPSHOT_ONLY_UNVERIFIED_COVERAGE"));
  }

  foreach(var item in items.OrderBy(x=>x.Kind)
     .ThenByDescending(x=>x.Score).ThenBy(x=>x.Source,StringComparer.OrdinalIgnoreCase)
     .ThenBy(x=>x.Url,StringComparer.OrdinalIgnoreCase))
  {
   ct.ThrowIfCancellationRequested();
   await output.WriteLineAsync(JsonlOutput.Serialize(item).AsMemory(),ct);
  }
  await output.FlushAsync(ct);
  return new OpportunityPreviewSummary(
   items.Count(x=>x.Kind==OpportunityKind.Employment),
   items.Count(x=>x.Kind==OpportunityKind.FreelanceProject),
   items.Count,
   items.Count(x=>x.Kind==OpportunityKind.Employment&&x.Bucket=="LikelyFit"),
   items.Count(x=>x.Kind==OpportunityKind.Employment&&x.Bucket=="NeedsReview"),
   items.Count(x=>x.Kind==OpportunityKind.Employment&&x.Bucket=="Excluded"),
   items.Count(x=>x.Kind==OpportunityKind.FreelanceProject&&x.Bucket=="Priority"),
   items.Count(x=>x.Kind==OpportunityKind.FreelanceProject&&x.Bucket=="Review"),
   items.Count(x=>x.Kind==OpportunityKind.FreelanceProject&&x.Bucket=="LowMatch"),
   inputCoverage,crawlCoverage,
   "Individual job discoveries are tracked only since Phase 15; null means unknown, not zero.",
   "Feeds, local JSONL exports, and recent stored crawl reports do NOT guarantee total coverage or an open position.");
 }

 private static bool TryParse(OpportunityInputKind kind,string json,out UnifiedOpportunity? item)
 {
  item=null;
  try
  {
   switch(kind)
   {
    case OpportunityInputKind.Freelancehunt:
    {
     var value=JsonSerializer.Deserialize<FreelancehuntCandidate>(json,JsonlOutput.Options);
     if(value?.Project is not { } project||
        !ValidProviderUrl(project.Url,"freelancehunt.com","/project/")||
        string.IsNullOrWhiteSpace(project.Title))return false;
     // Recompute project priority from fields instead of trusting a file's
     // Bucket claim. Includes Plus-only restrictions.
     var score=FreelancehuntProjects.Rank(project);
     item=new UnifiedOpportunity(OpportunityKind.FreelanceProject,"freelancehunt",
      project.Url,project.Title,null,score.Bucket,score.Score,
      project.Currency,project.Budget,project.Budget,project.PublishedAt,
      null,null,null,"Freelancehunt","OPEN_STATUS_UNVERIFIED",
      score.Reasons,["API JSONL snapshot; application eligibility and current open status require checking"]);
     return true;
    }
    case OpportunityInputKind.Freelancer:
    {
     var value=JsonSerializer.Deserialize<FreelancerCandidate>(json,JsonlOutput.Options);
     if(value?.Project is not { } project||
        !ValidProviderUrl(project.Url,"freelancer.com","/projects/")||
        string.IsNullOrWhiteSpace(project.Title))return false;
     var score=FreelancerProjects.Rank(project,value.Queries??[]);
     item=new UnifiedOpportunity(OpportunityKind.FreelanceProject,"freelancer",
      project.Url,project.Title,null,score.Bucket,score.Score,
      project.Currency,project.BudgetMinimum,project.BudgetMaximum,null,
      null,null,null,"Freelancer.com","OPEN_STATUS_UNVERIFIED",
      score.Reasons,["OAuth and express provider permission required for live scans; export snapshot is unverified"]);
     return true;
    }
    case OpportunityInputKind.RemoteBoards:
    {
     var value=JsonSerializer.Deserialize<RemoteBoardCandidate>(json,JsonlOutput.Options);
     if(value?.Opening is not { } opening||
        !ValidRemote(opening)||string.IsNullOrWhiteSpace(opening.Title))return false;
     var warnings=value.Warnings.ToList();
     var bucket=value.Bucket;
     // A serialized excerpt omits the full description used by the live
     // classifier: it cannot independently establish likely fit here.
     if(bucket==FitBucket.LikelyFit)
     {
      bucket=FitBucket.NeedsReview;
      warnings.Add("Original full description unavailable in compact snapshot; confidence downgraded");
     }
     item=new UnifiedOpportunity(OpportunityKind.Employment,
      opening.Source,opening.Url,opening.Title,opening.Company,
      bucket.ToString(),value.Score,null,null,null,opening.PublishedAt,
      null,null,null,opening.Attribution,"OPEN_STATUS_UNVERIFIED",
      value.Reasons,warnings);
     return true;
    }
   }
  }
  catch(JsonException){ }
  catch(ArgumentException){ }
  return false;
 }

 private static bool ValidRemote(RemoteBoardOpening item)=>
  item.Source switch
  {
   "remotive"=>ValidProviderUrl(item.Url,"remotive.com","/remote-jobs/"),
   "wwr-backend" or "wwr-programming"=>
    ValidProviderUrl(item.Url,"weworkremotely.com","/remote-jobs/"),
   _=>false
  };

 private static bool ValidProviderUrl(string? raw,string domain,string prefix)
 {
  if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri))return false;
  return uri.Scheme==Uri.UriSchemeHttps &&
   (uri.Host.Equals(domain,StringComparison.OrdinalIgnoreCase)||
    uri.Host.Equals("www."+domain,StringComparison.OrdinalIgnoreCase)) &&
   uri.AbsolutePath.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) &&
   uri.AbsolutePath.Length>prefix.Length &&
   string.IsNullOrEmpty(uri.UserInfo);
 }

 private static bool Key(OpportunityKind kind,string? raw,out string? key)
 {
  key=null;
  if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri)||
     uri.Scheme!=Uri.UriSchemeHttps||
     !string.IsNullOrEmpty(uri.UserInfo))return false;
  // Path case is preserved. Do not suppress query parameters because they
  // may identify distinct jobs on otherwise identical source paths.
  var builder=new UriBuilder(uri){Fragment=""};
  var normalized=builder.Uri.AbsoluteUri;
  if(builder.Query.Length==0)normalized=normalized.TrimEnd('/');
  key=kind+"|"+normalized;
  return true;
 }
}
