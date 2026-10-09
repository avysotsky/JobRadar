using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace JobRadar;

public sealed record RobotaDetail(string Description, DateTimeOffset? PublishedAt);
public sealed record RobotaCompanyFeed(
 IReadOnlyDictionary<long,RobotaDetail> Details,int RawRecords,int? ReportedTotal)
{
 public bool PossiblyTruncated => RawRecords>=100 ||
    (ReportedTotal.HasValue && ReportedTotal.Value>RawRecords);
}

/// <summary>
/// Reads public published-company vacancies. Some company feeds are capped at
/// 100 records; absence from a truncated feed NEVER proves vacancy closure.
/// The response is cached per company per scan.
/// </summary>
public sealed class RobotaCompanyDetails(HttpFetcher fetcher)
{
 private readonly Dictionary<long,Task<RobotaCompanyFeed>> cache=new();

 public async Task<RobotaDetail> GetAsync(JobRef job,CancellationToken ct)
 {
  var match=Regex.Match(job.Url,@"^https://robota\.ua/company(\d+)/vacancy(\d+)$",RegexOptions.IgnoreCase);
  if(!match.Success || !long.TryParse(match.Groups[1].Value,out var companyId)
      || !long.TryParse(match.Groups[2].Value,out var vacancyId))
    throw new InvalidDataException("Unknown Robota vacancy URL format");
  if(!cache.TryGetValue(companyId,out var pending))
  {
   pending=FetchCompanyAsync(companyId,ct);
   cache.Add(companyId,pending);
  }
  var company=await pending;
  if(!company.Details.TryGetValue(vacancyId,out var detail))
  {
   if(company.PossiblyTruncated)
    throw new InvalidDataException("Robota company feed potentially truncated: "+
      company.RawRecords+" returned, total "+(company.ReportedTotal?.ToString()??"unknown")+
      "; vacancy absent from limited response (NOT evidence vacancy closed): "+job.Url);
   throw new InvalidDataException("Vacancy not included in public published company feed (status unknown): "+job.Url);
  }
  return detail;
 }

 private async Task<RobotaCompanyFeed> FetchCompanyAsync(long companyId,CancellationToken ct)
 {
  string url="https://api.robota.ua/companies/"+companyId+"/published-vacancies";
  var json=await fetcher.GetAsync(url,ct);
  return ParseCompanyResult(json);
 }

 public static IReadOnlyDictionary<long,RobotaDetail> ParseCompanyFeed(string json)
  =>ParseCompanyResult(json).Details;

 public static RobotaCompanyFeed ParseCompanyResult(string json)
 {
  using var parsed=JsonDocument.Parse(json);
  if(!parsed.RootElement.TryGetProperty("filteredVacancies",out var vacancies)
      || vacancies.ValueKind!=JsonValueKind.Array)
    throw new InvalidDataException("Robota company feed lacks filteredVacancies array");
  int? reported=null;
  if(parsed.RootElement.TryGetProperty("totalVacanciesCount",out var count)
    && count.ValueKind==JsonValueKind.Number && count.TryGetInt32(out var total) && total>=0)
     reported=total;
  var results=new Dictionary<long,RobotaDetail>();
  var htmlParser=new HtmlParser();
  foreach(var record in vacancies.EnumerateArray())
  {
   if(!record.TryGetProperty("id",out var idValue) ||
      !long.TryParse(idValue.ToString(),out var id) || id<=0)continue;
   if(!record.TryGetProperty("description",out var descriptionValue) ||
      descriptionValue.ValueKind!=JsonValueKind.String)continue;
   var raw=descriptionValue.GetString()??"";
   var doc=htmlParser.ParseDocument(raw);
   foreach(var element in doc.QuerySelectorAll("script,style,nav,footer"))element.Remove();
   var text=Parsers.Clean(doc.Body?.TextContent ?? raw);
   if(text.Length<80)continue;
   DateTimeOffset? date=null;
   if(record.TryGetProperty("date",out var dateValue) &&
      DateTimeOffset.TryParse(dateValue.ToString(),CultureInfo.InvariantCulture,
                             DateTimeStyles.AssumeUniversal,out var published))
      date=published;
   results[id]=new RobotaDetail(text,date);
  }
  return new RobotaCompanyFeed(results,vacancies.GetArrayLength(),reported);
 }
}
