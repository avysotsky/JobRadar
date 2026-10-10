using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>
/// Conservative signals for senior ownership, frontend specialization and mobile
/// development. These trigger human review; they are never automatic proof that
/// a candidate is ineligible.
/// </summary>
public sealed record VacancyRoleSignals(
 bool SeniorRoleInDescription,
 bool FrontendOrFullstackCore,
 bool MobileDesktopCore)
{
 public bool RequiresReview => SeniorRoleInDescription || FrontendOrFullstackCore || MobileDesktopCore;
}

public static class VacancyRoleRequirements
{
 private const RegexOptions Flags=
  RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.CultureInvariant;

 // A senior level attached to the role being hired for, not an incidental
 // reference to senior colleagues, team leads or mentoring.
 private static readonly Regex SeniorRole=new(
  @"\b(?:looking\s+for|seeking|hiring|as\s+a|(?:the\s+)?role\s*[:\-]|position\s*[:\-])\s+(?:(?:a|an|the|experienced|strong|highly|qualified)\s+){0,4}(?:senior|sr\.?|principal|staff|tech(?:nical)?\s+lead|team\s+lead)\b",
  Flags);

 private static readonly Regex FullstackTitle=new(
  @"\b(?:full[\s-]?stack|frontend|front[\s-]?end|react(?:\.js)?|angular|vue(?:\.js)?)\b",
  Flags);
 private static readonly Regex RequiredFrontend=new(
  @"\b(?:required|mandatory|must\s+have|proficiency\s+(?:in|with)|strong\s+(?:experience|knowledge)\s+(?:in|with)|commercial\s+(?:front[\s-]?end|react|angular|vue)|experienced\s+in)\b.{0,100}\b(?:react(?:\.js)?|angular|vue(?:\.js)?|front[\s-]?end|next\.?js|javascript|typescript)\b"
  + @"|\b(?:react(?:\.js)?|angular|vue(?:\.js)?|front[\s-]?end|next\.?js)\b.{0,55}\b(?:required|mandatory|must\s+have|commercial\s+experience|essential)\b"
  + @"|\b(?:обов['’]язков[а-яіїє]*|обязательн[а-я]*)\b.{0,80}\b(?:react|angular|vue|frontend|фронтенд)\b",
  Flags);

 private static readonly Regex MobileDesktopTitle=new(
  @"\b(?:maui|xamarin|mobile\s+(?:app|application|developer|engineer|specialist)|wpf|winforms|windows\s+forms)\b",
  Flags);

 public static VacancyRoleSignals Analyze(string title,string description)
 {
  var cleanTitle=Normalize(title);
  var cleanBody=Normalize(description);
  return new VacancyRoleSignals(
   SeniorRole.IsMatch(cleanBody),
   FullstackTitle.IsMatch(cleanTitle)||RequiredFrontend.IsMatch(cleanBody),
   MobileDesktopTitle.IsMatch(cleanTitle));
 }

 private static string Normalize(string text)=>
  Regex.Replace(System.Net.WebUtility.HtmlDecode(text),@"\s+"," ").Trim();
}
