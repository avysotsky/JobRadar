using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobRadar;

/// <summary>
/// Serialization for standalone JSONL files (not HTML/script embedding).
/// Leave ordinary JSON escaping for control characters and quotes, while
/// retaining readable Cyrillic text in the UTF-8 export.
/// </summary>
public static class JsonlOutput
{
 public static readonly JsonSerializerOptions Options=new()
 {
  Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  Converters={new JsonStringEnumConverter()}
 };

 public static string Serialize<T>(T value) => JsonSerializer.Serialize(value,Options);

 /// <summary>
 /// Human-review projection: keep scores and evidence, omit verbose source text.
 /// This mirrors the standalone compact audit produced for the 265-vacancy run.
 /// </summary>
 public static CompactVacancy Compact(VacancyAssessment item)
 {
  var job=item.Vacancy;
  return new CompactVacancy(job.Source,job.Url,job.Title,job.Company,
   item.Bucket,item.Score,item.WorkMode,job.HasFullText,job.IsOpen,
   item.Reasons,item.Warnings);
 }
}

public sealed record CompactVacancy(
 string Source,string Url,string Title,string? Company,
 FitBucket Bucket,int Score,WorkMode WorkMode,bool HasFullText,bool? IsOpen,
 IReadOnlyList<string> Reasons,IReadOnlyList<string> Warnings);
