using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobRadar;

public sealed record TriageExportResult(string AuditFile,string ShortlistFile,int Evaluated,
 int LikelyFit,int NeedsReview,int Excluded,int Shortlisted);

public static class TriageExport
{
 private static readonly JsonSerializerOptions Json=new()
 {
  Converters={new JsonStringEnumConverter()}
 };

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
  string shortlist=Path.Combine(directory,$"triage-shortlist-{stamp}.jsonl");
  int shortlistCount=0;
  await using(var all=new StreamWriter(audit,false,new UTF8Encoding(false)))
  await using(var selected=new StreamWriter(shortlist,false,new UTF8Encoding(false)))
  {
   foreach(var item in evaluated)
   {
    ct.ThrowIfCancellationRequested();
    var json=JsonSerializer.Serialize(item,Json);
    await all.WriteLineAsync(json.AsMemory(),ct);
    if(item.Bucket!=FitBucket.Excluded)
    {
     await selected.WriteLineAsync(json.AsMemory(),ct);
     shortlistCount++;
    }
   }
  }
  return new TriageExportResult(audit,shortlist,evaluated.Length,
    evaluated.Count(x=>x.Bucket==FitBucket.LikelyFit),
    evaluated.Count(x=>x.Bucket==FitBucket.NeedsReview),
    evaluated.Count(x=>x.Bucket==FitBucket.Excluded),shortlistCount);
 }
}
