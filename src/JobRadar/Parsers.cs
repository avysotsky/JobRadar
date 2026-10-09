using AngleSharp.Html.Parser;
using AngleSharp.Dom;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobRadar;
public static class Parsers
{
 private static readonly HtmlParser Parser = new();
 public static IReadOnlyList<JobRef> Listings(string source, string html, Uri root)
 {
  var doc = Parser.ParseDocument(html);
  var list = new List<JobRef>();
  var selector = source == "dou" ? "a.vt, .vacancy a.vt, .l-vacancy a" : "a[href*='/jobs/']";
  foreach(var a in doc.QuerySelectorAll(selector))
  {
   var href=a.GetAttribute("href");
   if(string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(root,href,out var uri)) continue;
   if(uri.Host != root.Host) continue;
   if(source=="dou" && !Regex.IsMatch(uri.AbsolutePath,@"^/vacancies/\d+/?$")) continue;
   if(source=="djinni" && !Regex.IsMatch(uri.AbsolutePath,@"^/jobs/\d+[-/].*|^/jobs/\d+/?$")) continue;
   var title=Clean(a.TextContent);
   if(title.Length<3) continue;
   var row=a.Closest("li") ?? a.ParentElement;
   var company=row?.QuerySelector(".company, .company-name, .company-name a, .company-link")?.TextContent;
   list.Add(new JobRef(source,uri.GetLeftPart(UriPartial.Path),title,CleanOrNull(company),null));
  }
  return list.DistinctBy(x=>x.Url).ToList();
 }
 public static string Description(string source, string html)
 {
  var doc=Parser.ParseDocument(html);
  foreach(var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
  {
   try
   {
    using var json=JsonDocument.Parse(script.TextContent);
    var found=FindJobDescription(json.RootElement);
    if(!string.IsNullOrWhiteSpace(found))
    {
     var fragment=Parser.ParseDocument(found);
     return Clean(fragment.Body?.TextContent ?? found);
    }
   } catch(JsonException) { }
  }
  if(source.StartsWith("robota-",StringComparison.OrdinalIgnoreCase))return RobotaParser.Description(html);
  if(source.StartsWith("workua-",StringComparison.OrdinalIgnoreCase))return WorkUaParser.Description(html);
  var selectors = source=="dou" ? new[]{".vacancy-section", ".b-vacancy__description", "article"} : new[]{".job-description", "[data-testid='job-description']", ".job-post__description", "article"};
  foreach(var selector in selectors)
  {
   var el=doc.QuerySelector(selector);
   if(el is null) continue;
   foreach(var dead in el.QuerySelectorAll("script,style,nav,footer")) dead.Remove();
   var value=Clean(el.TextContent);
   if(value.Length>=80) return value;
  }
  return "";
 }
 public static bool? OpenStatus(string html)
 {
  var doc=Parser.ParseDocument(html);
  var t=Clean(doc.Body?.TextContent ?? "").ToLowerInvariant();
  if(new[]{"вакансія завершена", "вакансія закрита", "ця вакансія вже завершена", "job is closed", "position is closed", "no longer accepting applications"}.Any(t.Contains)) return false;
  return null;
 }
 public static int? ReportedTotal(string html)
 {
  var doc=Parser.ParseDocument(html);
  var t=Clean(doc.Body?.TextContent ?? "");
  var m=Regex.Match(t,@"(?i)([\d\s]+)\s+(?:вакансі[йя]|jobs|вакансий)");
  return m.Success && int.TryParse(Regex.Replace(m.Groups[1].Value,@"\s", ""),out int n)?n:null;
 }
 private static string? FindJobDescription(JsonElement x)
 {
  if(x.ValueKind==JsonValueKind.Array){foreach(var e in x.EnumerateArray()){var r=FindJobDescription(e);if(r!=null)return r;}}
  if(x.ValueKind!=JsonValueKind.Object)return null;
  if(x.TryGetProperty("@graph",out var graph)){var r=FindJobDescription(graph);if(r!=null)return r;}
  if(x.TryGetProperty("@type",out var typ) && typ.ToString().Contains("JobPosting",StringComparison.OrdinalIgnoreCase) && x.TryGetProperty("description",out var d))return d.ToString();
  return null;
 }
 public static string Clean(string? s)=>Regex.Replace(System.Net.WebUtility.HtmlDecode(s??""),@"\s+"," ").Trim();
 private static string? CleanOrNull(string? s){var v=Clean(s);return v.Length==0?null:v;}
}