using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace JobRadar;

/// <summary>
/// Provider-page observation. UNKNOWN is not evidence of an open job.
/// Absence from a search or company feed is never a closure signal.
/// </summary>
public sealed record VacancyStatusObservation(bool? IsOpen,string EvidenceCode);

public static class VacancyStatusEvidence
{
 private static readonly Regex Closed=new(
  @"(?:ця\s+вакансія\s+вже\s+завершена|вакансія\s+(?:завершена|закрита)|"
  + @"эта\s+вакансия\s+(?:закрыта|завершена)|вакансия\s+(?:закрыта|завершена)|"
  + @"(?:job|position)\s+is\s+closed|no\s+longer\s+accepting\s+applications)",
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);

 // Provider-style page banners only. A job description may DISCUSS a
 // closed position; this is not evidence that this advertisement is closed.
 private const string BannerSelectors=
  ".vacancy-closed,.job-closed,.vacancy-status,.job-status,"
  + "[role='alert'],.alert,[data-testid='job-status'],"
  + "[data-testid='vacancy-status']";

 public static VacancyStatusObservation FromProviderHtml(string html)
 {
  var doc=new HtmlParser().ParseDocument(html);
  foreach(var banner in doc.QuerySelectorAll(BannerSelectors))
  {
   if(banner.Closest("article,.vacancy-section,.job-description,[data-testid='job-description']") is not null)
    continue;
   var value=Parsers.Clean(banner.TextContent);
   if(value.Length is >0 and <=500 && Closed.IsMatch(value))
    return new VacancyStatusObservation(false,"html:explicit-closed-banner");
  }
  foreach(var ignored in doc.QuerySelectorAll("script,style,footer,nav,aside"))ignored.Remove();
  var body=Parsers.Clean(doc.Body?.TextContent??"");
  // This fallback covers explicit, short standalone closed-message pages.
  // Never scan all text on a normal job page for arbitrary closure phrases.
  var standaloneClosure=Closed.Match(body);
  if(body.Length is >0 and <=160 && standaloneClosure.Success && standaloneClosure.Index<=12
     && doc.QuerySelector("article,.vacancy-section,.job-description,[data-testid='job-description']") is null)
   return new VacancyStatusObservation(false,"html:standalone-closed-message");
  return new VacancyStatusObservation(null,"html:status-unverified");
 }
}
