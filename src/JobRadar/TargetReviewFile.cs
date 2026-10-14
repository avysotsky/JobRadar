using System.Text;
using System.Text.Json;

namespace JobRadar;

/// <summary>
/// A local, read-only re-evaluation of the existing "triage-all" JSONL export.
/// No PostgreSQL connection, provider HTTP, or employment-decision mutation.
/// </summary>
public sealed record OfflineTargetReviewReport(
 string InputKind, int SourceRows, bool ManualDecisionsAvailable,
 string SourceCompleteness, string Interpretation,
 TargetReviewReport Review);

public static class TargetReviewFile
{
 public const int MaxRows=50000;
 public const long MaxFileBytes=64L*1024*1024;
 public const int MaxLineChars=2*1024*1024;

 private sealed record ExportRow(VacancySnapshot? Vacancy);

 public static async Task<OfflineTargetReviewReport> AnalyzeFileAsync(
  string path,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(path))
   throw new ArgumentException("An explicit input JSONL file is required",nameof(path));
  var info=new FileInfo(path);
  if(!info.Exists)
   throw new FileNotFoundException("Offline triage JSONL not found",path);
  if(info.Length==0 || info.Length>MaxFileBytes)
   throw new InvalidDataException("JSONL file is empty or exceeds the 64 MiB input bound");

  var vacancies=new List<VacancySnapshot>();
  var seen=new HashSet<string>(StringComparer.Ordinal);
  await using var file=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,
   FileShare.Read,4096,FileOptions.Asynchronous|FileOptions.SequentialScan);
  using var reader=new StreamReader(file,new UTF8Encoding(false,true),
   detectEncodingFromByteOrderMarks:false);
  var lineNumber=0;
  while(await reader.ReadLineAsync(ct) is { } line)
  {
   lineNumber++;
   if(line.Length>MaxLineChars)
    throw new InvalidDataException("JSONL row exceeds the 2 Mi-character bound");
   if(string.IsNullOrWhiteSpace(line))continue;
   if(vacancies.Count>=MaxRows)
    throw new InvalidDataException("JSONL exceeds the 50,000-row input bound");
   ExportRow? row;
   try { row=JsonSerializer.Deserialize<ExportRow>(line,JsonlOutput.Options); }
   catch(JsonException e)
   {
    throw new InvalidDataException(
     "Malformed triage JSONL at row "+lineNumber,e);
   }
   var vacancy=row?.Vacancy;
   if(vacancy is null || string.IsNullOrWhiteSpace(vacancy.Source) ||
      string.IsNullOrWhiteSpace(vacancy.Title) ||
      string.IsNullOrWhiteSpace(vacancy.Url) ||
      vacancy.Description is null || vacancy.Queries is null ||
      vacancy.LastSeen==default)
    throw new InvalidDataException(
     "Incomplete vacancy evidence at JSONL row "+lineNumber);
   if(!Uri.TryCreate(vacancy.Url,UriKind.Absolute,out var url) ||
      url.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(url.Host))
    throw new InvalidDataException(
     "Invalid source URL at JSONL row "+lineNumber);
   if(!seen.Add(vacancy.Source+"\u0000"+vacancy.Url))
    throw new InvalidDataException(
     "Duplicate source/URL in JSONL at row "+lineNumber);
   vacancies.Add(vacancy);
  }
  if(vacancies.Count==0)
   throw new InvalidDataException("Offline triage JSONL has no vacancy records");
  // Re-run the same review gates used by the database mode. Do NOT trust the
  // exported Score/Bucket/WorkMode fields, which may be stale or manipulated.
  var review=TargetReview.Analyze(vacancies,
   new Dictionary<string,EmploymentDecision>(),vacancies.Count,
   DateTimeOffset.UtcNow);
  return new OfflineTargetReviewReport(
   "TRIAGE_ALL_JSONL",vacancies.Count,false,
   "UNKNOWN: input is a historical export, not proof of complete live coverage",
   "Vacancies are re-evaluated using current Phase 39 gates, not sorted by "+
   "the exported Score. This file does NOT include user Applied/Inactive "+
   "decisions, so already-applied roles may appear. There is no live status "+
   "or Ukraine work-eligibility verification. Check each original posting "+
   "and your personal application history before applying.",
   review);
 }
}
