using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace JobRadar;

/// <summary>
/// Best-effort, unauthenticated Work.ua public search adapter.
/// Disabled by default until the site's permission and live response are verified.
/// No login, CAPTCHA bypass or browser automation is performed.
/// </summary>
public sealed class WorkUaSource(string query) : IJobSource
{
 public string Name=>"workua-"+query;
 public Uri Home=>new("https://www.work.ua/");
 public string ListingUrl(int page)
 {
  if(page<0)throw new ArgumentOutOfRangeException(nameof(page));
  var path="https://www.work.ua/jobs-remote-"+Uri.EscapeDataString(query)+"/";
  return page==0?path:path+"?page="+(page+1);
 }
 public IReadOnlyList<JobRef> ParseListings(string html)=>WorkUaParser.Listings(html);
}

public static class WorkUaParser
{
 private static readonly HtmlParser Html=new();
 private static readonly Regex VacancyPath=new(@"^/jobs/([0-9]+)/?$",RegexOptions.Compiled|RegexOptions.CultureInvariant);
 public static IReadOnlyList<JobRef> Listings(string html)
 {
  var doc=Html.ParseDocument(html);
  var jobs=new List<JobRef>();
  foreach(var a in doc.QuerySelectorAll("a[href]"))
  {
   var href=a.GetAttribute("href");
   if(!Uri.TryCreate(new Uri("https://www.work.ua/"),href,out var uri))continue;
   if(uri.Scheme!="https" || uri.Host!="www.work.ua")continue;
   if(!VacancyPath.IsMatch(uri.AbsolutePath))continue;
   var title=Parsers.Clean(a.QuerySelector("h2,h3,h4")?.TextContent??a.TextContent);
   if(title.Length<4 || title.Length>240)continue;
   jobs.Add(new JobRef("workua",uri.GetLeftPart(UriPartial.Path),title,null,null));
  }
  return jobs.DistinctBy(j=>j.Url).ToArray();
 }
 public static string Description(string html)
 {
  var doc=Html.ParseDocument(html);
  foreach(var selector in new[]{"[itemprop='description']","#job-description",
       ".job-description","[data-qa='job-description']",".card [class*='description']"})
  {
   var el=doc.QuerySelector(selector);
   if(el is null)continue;
   foreach(var dead in el.QuerySelectorAll("script,style,nav,footer"))dead.Remove();
   var text=Parsers.Clean(el.TextContent);
   if(text.Length>=80)return text;
  }
  return "";
 }
 public static DateTimeOffset? PublishedDate(string html)
 {
  var doc=Html.ParseDocument(html);
  foreach(var item in doc.QuerySelectorAll("meta[itemprop='datePosted'],time[datetime]"))
  {
   var raw=item.GetAttribute("content")??item.GetAttribute("datetime");
   if(DateTimeOffset.TryParse(raw,out var parsed))return parsed;
  }
  return null; // relative timestamps are intentionally NOT guessed
 }
}
