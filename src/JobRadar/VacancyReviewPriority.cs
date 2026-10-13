using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>Order human-review candidates without changing their fit or open-status claims.</summary>
public enum VacancyReviewTier { TargetDotNetBackend=0, PartialDotNetEvidence=1,
 DifficultRequirements=2, OtherSpecialization=3 }

public sealed record VacancyReviewRanking(VacancyReviewTier Tier,string Reason);

public static class VacancyReviewPriority
{
 private const RegexOptions Flags=RegexOptions.IgnoreCase|
  RegexOptions.CultureInvariant|RegexOptions.Compiled;
 private static readonly Regex DotNet=new(
  @"(?<![a-z0-9])(?:asp\.net|\.net|dotnet|c#|csharp|c[- ]sharp)(?![a-z0-9])",Flags);
 private static readonly Regex Backend=new(
  @"\b(?:backend|back[- ]end|web\s*api|rest\s*api|api\s+developer|бекенд|бэкенд)\b",Flags);
 private static readonly Regex DesktopOrFrontendTitle=new(
  @"\b(?:full[\s-]?stack|front[\s-]?end|wpf|maui|xamarin|"
  + @"avalonia|react(?:\.js)?|angular|vue(?:\.js)?)\b",Flags);
 private static readonly Regex MixedSenior=new(
  @"\b(?:senior|sr\.?|principal|staff|lead|architect)\b",Flags);
 private static readonly Regex RequiredAdvancedEnglish=new(
  @"(?:english|англійськ[а-яіїє]*|английск[а-я]*)[^.;\n]{0,45}"
  + @"\b(?:b2|c1|c2|upper[ -]intermediate|advanced)\b"
  + @"|\b(?:b2|c1|c2)\b[^.;\n]{0,30}"
  + @"(?:english|англійськ|английск)",Flags);

 public static VacancyReviewRanking Assess(VacancyAssessment candidate)
 {
  var job=candidate.Vacancy;
  var title=job.Title;
  var body=job.Description.Length>0?job.Description:job.Preview??"";
  var text=title+" "+body;
  if(DesktopOrFrontendTitle.IsMatch(title)||
     VacancyRoleRequirements.Analyze(title,body).FrontendOrFullstackCore)
   return new(VacancyReviewTier.OtherSpecialization,
    "Основная специализация FullStack, UI, mobile или desktop");

  if(MixedSenior.IsMatch(title)||
     VacancyRoleRequirements.Analyze(title,body).SeniorRoleInDescription||
     RequiredAdvancedEnglish.IsMatch(text)||
     candidate.WorkMode is WorkMode.Hybrid or WorkMode.Onsite)
   return new(VacancyReviewTier.DifficultRequirements,
    "Сложный уровень, английский B2+ или ограничения формата");

  var net=DotNet.IsMatch(text);
  var backend=Backend.IsMatch(text);
  if(net && backend)
   return new(VacancyReviewTier.TargetDotNetBackend,
    "Целевой .NET Backend — проверять первым, даже при неизвестном open-статусе");
  if(net)
   return new(VacancyReviewTier.PartialDotNetEvidence,
    "Есть .NET, но Backend как основная специализация не подтверждён");
  return new(VacancyReviewTier.OtherSpecialization,
   "Нет явных подтверждений целевого .NET Backend");
 }
}
