using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace JobRadar;

public sealed record RobotaDetail(string Description, DateTimeOffset? PublishedAt);

/// <summary>
/// Loads descriptions from Robota's publicly published company vacancies feed.
/// A company response can be large; cache it per scan and never confuse a missing
/// detail with a successful full-text fetch.
/// </summary>
public sealed class RobotaCompanyDetails(HttpFetcher fetcher)
{
 private readonly Dictionary<long,Task<IReadOnlyDictionary<long,RobotaDetail>>> cache=new();

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
  var vacancies=await pending;
  if(!vacancies.TryGetValue(vacancyId,out var detail))
    throw new InvalidDataException("Vacancy not included in public published company feed: "+job.Url);
  return detail;
 }

 private async Task<IReadOnlyDictionary<long,RobotaDetail>> FetchCompanyAsync(long companyId,CancellationToken ct)
 {
  string url="https://api.robota.ua/companies/"+companyId+"/published-vacancies";
  var json=await fetcher.GetAsync(url,ct);
  return ParseCompanyFeed(json);
 }

 public static IReadOnlyDictionary<long,RobotaDetail> ParseCompanyFeed(string json)
 {
  using var parsed=JsonDocument.Parse(json);
  if(!parsed.RootElement.TryGetProperty("filteredVacancies",out var vacancies)
      || vacancies.ValueKind!=JsonValueKind.Array)
    throw new InvalidDataException("Robota company feed lacks filteredVacancies array");
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
  return results;
 }
}
