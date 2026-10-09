using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
namespace JobRadar;

/// <summary>
/// Unauthenticated public HTML adapter for Robota.ua search pages.
/// Search pages are not a contract: until paging and totals are observed live,
/// every crawl must be described as coverage-unverified.
/// </summary>
public sealed class RobotaSource(string query) : IJobSource
{
 public string Name => "robota-" + query;
 public Uri Home => new("https://robota.ua/");
 public string ListingUrl(int page)
 {
  if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
  string baseUrl = "https://robota.ua/zapros/" + Uri.EscapeDataString(query) + "/ukraine";
  return page == 0 ? baseUrl : baseUrl + "?page=" + (page + 1);
 }
 public IReadOnlyList<JobRef> ParseListings(string html) => RobotaParser.Listings(Name, html);
}

public static class RobotaParser
{
 private static readonly Regex VacancyPath = new(
  @"^/(?:ua/|ru/)?company[0-9]+/vacancy[0-9]+/?$",
  RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
 private static readonly HtmlParser Html = new();

 public static IReadOnlyList<JobRef> Listings(string source, string html)
 {
  var doc = Html.ParseDocument(html);
  var jobs = new List<JobRef>();
  foreach (var a in doc.QuerySelectorAll("a[href]"))
  {
   var href = a.GetAttribute("href");
   if (!Uri.TryCreate(new Uri("https://robota.ua/"), href, out var uri)) continue;
   if (uri.Scheme != Uri.UriSchemeHttps ||
       !string.Equals(uri.Host, "robota.ua", StringComparison.OrdinalIgnoreCase)) continue;
   if (!VacancyPath.IsMatch(uri.AbsolutePath)) continue;
   var heading = a.QuerySelector("h2, h3, h4, [data-testid='vacancy-title']");
   var title = Parsers.Clean(heading?.TextContent ?? a.TextContent);
   if (title.Length < 4) continue;
   if (title.Length > 240) title = title[..240];
   // Company cannot be safely inferred from free text in a large listing card.
   jobs.Add(new JobRef(source, uri.GetLeftPart(UriPartial.Path), title, null, null));
  }
  return jobs.DistinctBy(x => x.Url).ToArray();
 }

 public static string Description(string html)
 {
  var doc = Html.ParseDocument(html);
  string[] selectors = [
    "[itemprop='description']", "[data-testid='vacancy-description']",
    "[data-qa='vacancy-description']", ".vacancy-description",
    ".vacancy__description", "[class*='vacancy-description']", "article"];
  foreach (var selector in selectors)
  {
   var element = doc.QuerySelector(selector);
   if (element is null) continue;
   foreach (var dead in element.QuerySelectorAll("script, style, nav, footer")) dead.Remove();
   var content = Parsers.Clean(element.TextContent);
   if (content.Length >= 100) return TrimRelated(content);
  }
  // Last-resort extraction. The result is explicitly unverified as to coverage;
  // this fallback requires a vacancy heading and strips adjacent recommendations.
  var main = doc.QuerySelector("main");
  if (main?.QuerySelector("h1") is null) return "";
  var text = Parsers.Clean(main.TextContent);
  var title = Parsers.Clean(main.QuerySelector("h1")!.TextContent);
  int titlePos = text.IndexOf(title, StringComparison.OrdinalIgnoreCase);
  if (titlePos >= 0) text = text[titlePos..];
  text = TrimRelated(text);
  return text.Length >= 150 ? text : "";
 }

 private static string TrimRelated(string value)
 {
  foreach (var marker in new[] { "Схожі вакансії", "Похожие вакансии", "Гарячі вакансії",
                                  "Отримуйте нові вакансії", "Передзвоніть мені" })
  {
   int i = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
   if (i >= 120) value = value[..i];
  }
  return value.Trim();
 }

 public static DateTimeOffset? PublishedDate(string html)
 {
  var doc = Html.ParseDocument(html);
  foreach(var node in doc.QuerySelectorAll("meta[itemprop='datePosted'], time[datetime]"))
  {
   var value=node.GetAttribute("content") ?? node.GetAttribute("datetime");
   if(DateTimeOffset.TryParse(value, out var date)) return date;
  }
  // Website often supplies publication day in Ukrainian next to the title.
  var content=Parsers.Clean(doc.Body?.TextContent ?? "");
  var match=Regex.Match(content, @"\b([0-3]?\d)\s+(січня|лютого|березня|квітня|травня|червня|липня|серпня|вересня|жовтня|листопада|грудня)\s+(20\d{2})\b",RegexOptions.IgnoreCase);
  if(!match.Success)return null;
  string[] months=["січня","лютого","березня","квітня","травня","червня","липня","серпня","вересня","жовтня","листопада","грудня"];
  var month=Array.FindIndex(months,m=>string.Equals(m,match.Groups[2].Value,StringComparison.OrdinalIgnoreCase))+1;
  if(month==0)return null;
  // Date-only source has no timestamp. Midnight UTC is an explicit date-only encoding.
  try{return new DateTimeOffset(int.Parse(match.Groups[3].Value),month,int.Parse(match.Groups[1].Value),0,0,0,TimeSpan.Zero);}
  catch(ArgumentOutOfRangeException){return null;}
 }
}
