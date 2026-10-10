using System.Text;
using System.Text.Json;

namespace JobRadar;

/// <summary>
/// Offline re-ranking of a previously collected remote-board JSONL export.
/// This never fetches provider pages or calls PostgreSQL.
/// </summary>
public static class RemoteReviewOffline
{
 public static async Task<RemoteOfflineReviewReport> RunAsync(
  string path,TextWriter output,CancellationToken ct)
 {
  var integrity=await RemoteJsonlIntegrity.CheckAsync(path,ct);
  if(!integrity.Valid)
   throw new InvalidDataException(
    "Remote JSONL integrity validation failed; cannot prioritize damaged input");

  var items=new List<RemoteBoardCandidate>();
  int total=0,excluded=0,likely=0;
  using(var reader=new StreamReader(path,new UTF8Encoding(false,true)))
  {
   while(await reader.ReadLineAsync(ct) is { } line)
   {
    if(string.IsNullOrWhiteSpace(line))continue;
    var candidate=JsonSerializer.Deserialize<RemoteBoardCandidate>(
      line,JsonlOutput.Options) ??
      throw new InvalidDataException("Remote JSONL row is null");
    if(candidate.Opening is null)
     throw new InvalidDataException("Remote JSONL row has no opening");

    total++;
    if(candidate.Bucket==FitBucket.Excluded){excluded++;continue;}
    if(candidate.Bucket==FitBucket.LikelyFit){likely++;continue;}

    // The full received RSS/Remotive text is intentionally not present in
    // compact JSONL. Preserve the live review tier if already supplied;
    // otherwise derive a conservative tier from the serialized excerpt.
    if(candidate.ReviewPriority==RemoteReviewPriority.NotApplicable)
    {
     var review=RemoteReviewTriage.Assess(candidate.Opening,candidate.Bucket);
     candidate=candidate with
      {ReviewPriority=review.Priority,ReviewEvidence=review.Evidence};
    }
    items.Add(candidate);
   }
  }

  foreach(var item in items.OrderBy(x=>x.ReviewPriority)
    .ThenByDescending(x=>x.Score)
    .ThenBy(x=>x.Opening.Source,StringComparer.OrdinalIgnoreCase)
    .ThenBy(x=>x.Opening.Url,StringComparer.OrdinalIgnoreCase))
  {
   ct.ThrowIfCancellationRequested();
   // Return only compact pointers for an operator's private shortlist.
   await output.WriteLineAsync(JsonlOutput.Serialize(new
   {
    item.Opening.Source,item.Opening.Url,item.Opening.Title,
    item.Opening.Company,item.Opening.Attribution,
    item.Opening.CandidateLocation,item.Opening.PublishedAt,
    item.Bucket,item.Score,item.ReviewPriority,item.ReviewEvidence,
    Status="UNVERIFIED_OPEN_AND_WORK_LOCATION",
    ReviewBasis=item.Opening.HasFullText
      ?"Compact API snapshot, full text omitted":"RSS/preview snapshot, full posting unverified"
   }).AsMemory(),ct);
  }
  await output.FlushAsync(ct);
  return new RemoteOfflineReviewReport(
   total,items.Count,likely,excluded,
   RemoteReviewTriage.Summarize(items),
   "Review priorities do not verify current opening, working from Ukraine, or complete original posting text.");
 }
}

public sealed record RemoteOfflineReviewReport(
 int Total,int NeedsReview,int LikelyFit,int Excluded,
 RemoteReviewBreakdown ReviewBreakdown,string ScopeWarning);
