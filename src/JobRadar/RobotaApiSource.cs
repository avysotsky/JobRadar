using System.Globalization;
using System.Text.Json;

namespace JobRadar;

/// <summary>
/// Reads public JSON search results exposed by api.rabota.ua.
/// The endpoint and its paging contract must be verified on live responses.
/// </summary>
public sealed class RobotaApiSource(string query) : IJobSource
{
 public string Name=>"robota-api-"+query;
 public Uri Home=>new("https://api.rabota.ua/");
 public string ListingUrl(int page)
 {
  if(page<0)throw new ArgumentOutOfRangeException(nameof(page));
  return "https://api.rabota.ua/vacancy/search?keyWords="+Uri.EscapeDataString(query)+
    "&count=59&page="+page.ToString(CultureInfo.InvariantCulture);
 }
 public IReadOnlyList<JobRef> ParseListings(string payload)
 {
  using var doc=JsonDocument.Parse(payload);
  if(!doc.RootElement.TryGetProperty("documents",out var documents) ||
     documents.ValueKind!=JsonValueKind.Array)
    throw new InvalidDataException("Robota API response missing documents array");
  var list=new List<JobRef>();
  foreach(var item in documents.EnumerateArray())
  {
   if(!item.TryGetProperty("id",out var idElement) ||
      !item.TryGetProperty("notebookId",out var notebookElement))continue;
   var id=idElement.ToString();var notebook=notebookElement.ToString();
   if(!long.TryParse(id,out var jobId) || jobId<=0 ||
      !long.TryParse(notebook,out var companyId) || companyId<=0)continue;
   var title=GetString(item,"name");
   if(string.IsNullOrWhiteSpace(title))continue;
   var company=GetString(item,"companyName");
   DateTimeOffset? published=null;
   if(DateTimeOffset.TryParse(GetString(item,"date"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var time))
     published=time;
   string url="https://robota.ua/company"+companyId+"/vacancy"+jobId;
   var previewHtml=GetString(item,"shortDescription")??"";
   var preview=Parsers.Clean(System.Text.RegularExpressions.Regex.Replace(previewHtml,@"<[^>]+>"," "));
   list.Add(new JobRef("robota",url,title,company,published,preview.Length==0?null:preview));
  }
  return list.DistinctBy(x=>x.Url).ToArray();
 }
 /// <summary>The number of raw records in the provider JSON page, before validation.</summary>
 public int RawDocumentCount(string payload)
 {
  using var doc=JsonDocument.Parse(payload);
  if(!doc.RootElement.TryGetProperty("documents",out var documents) ||
     documents.ValueKind!=JsonValueKind.Array)
   throw new InvalidDataException("Robota API response missing documents array");
  return documents.GetArrayLength();
 }
 public int? ReportedTotal(string payload)
 {
  using var doc=JsonDocument.Parse(payload);
  if(!doc.RootElement.TryGetProperty("total",out var total))return null;
  return total.ValueKind==JsonValueKind.Number && total.TryGetInt32(out var n) && n>=0?n:null;
 }
 private static string? GetString(JsonElement element,string name)
 {
  if(!element.TryGetProperty(name,out var value)||value.ValueKind==JsonValueKind.Null)return null;
  return value.ToString();
 }
}
