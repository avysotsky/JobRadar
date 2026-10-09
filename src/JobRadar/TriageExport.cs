using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobRadar;

public sealed record TriageExportResult(
 string AuditFile,string ShortlistFile,string ReviewFile,int Evaluated,
 int LikelyFit,int NeedsReview,int Excluded,int Shortlisted);

public static class TriageExport
{
 private static readonly JsonSerializerOptions Json=new()
 {
  Converters={new JsonStringEnumConverter()}
 };

 /// <summary>
 /// No candidates are discarded: audit contains everything, shortlist only
 /// strong text-supported matches, and uncertain cases go to human review.
 /// </summary>
 public static async Task<TriageExportResult> WriteAsync(
  Storage storage,string directory,int maxRecords,CancellationToken ct)
 {
  var records=await storage.ReadVacanciesForTriageAsync(maxRecords,ct);
  var evaluated=records.Select(VacancyTriage.Assess)
   .OrderBy(x=>x.Bucket).ThenByDescending(x=>x.Score)
   .ThenByDescending(x=>x.Vacancy.LastSeen).ToArray();
  Directory.CreateDirectory(directory);
  string stamp=DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
  string audit=Path.Combine(directory,$"triage-all-{stamp}.jsonl");
  string shortlist=Path.Combine(directory,$"triage-remote-likely-{stamp}.jsonl");
  string review=Path.Combine(directory,$"triage-review-{stamp}.jsonl");
  await using(var all=new StreamWriter(audit,false,new UTF8Encoding(false)))
  await using(var selected=new StreamWriter(shortlist,false,new UTF8Encoding(false)))
  await using(var needsReview=new StreamWriter(review,false,new UTF8Encoding(false)))
  {
   foreach(var item in evaluated)
   {
    ct.ThrowIfCancellationRequested();
    var json=JsonSerializer.Serialize(item,Json);
    await all.WriteLineAsync(json.AsMemory(),ct);
    if(item.Bucket==FitBucket.LikelyFit)
     await selected.WriteLineAsync(json.AsMemory(),ct);
    else if(item.Bucket==FitBucket.NeedsReview)
     await needsReview.WriteLineAsync(json.AsMemory(),ct);
   }
  }
  int likely=evaluated.Count(x=>x.Bucket==FitBucket.LikelyFit);
  return new TriageExportResult(audit,shortlist,review,evaluated.Length,
    likely,evaluated.Count(x=>x.Bucket==FitBucket.NeedsReview),
    evaluated.Count(x=>x.Bucket==FitBucket.Excluded),likely);
 }
}
