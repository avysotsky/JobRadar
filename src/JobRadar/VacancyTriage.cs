using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>Evidence-backed screening, never an assertion that a vacancy is available.</summary>
public enum WorkMode { Remote, Hybrid, Onsite, Unknown }
public enum FitBucket { LikelyFit, NeedsReview, Excluded }

public sealed record VacancySnapshot(
 string Source,string Url,string Title,string? Company,string? Preview,
 string Description,bool HasFullText,bool? IsOpen,DateTimeOffset? PublishedAt,
 DateTimeOffset LastSeen,IReadOnlyList<string> Queries,
 DateTimeOffset? StatusCheckedAt=null,string? StatusEvidence=null);

public sealed record VacancyAssessment(
 VacancySnapshot Vacancy,WorkMode WorkMode,FitBucket Bucket,int Score,
 IReadOnlyList<string> Reasons,IReadOnlyList<string> Warnings);

public static class VacancyTriage
{
 private static readonly RegexOptions Flags =
  RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled;

 private static readonly Regex Net = new(
  @"(?<![a-z0-9])(?:asp\.net|\.net|dotnet|c#|csharp|c[- ]sharp)(?![a-z0-9])",Flags);
 private static readonly Regex Middle = new(
  @"\b(?:middle|mid[- ]level|junior[- ]strong|strong[- ]junior|мідл|мидл|середн(?:ій|его)\s+рів(?:ень|ня))\b",Flags);
 private static readonly Regex SeniorTitle = new(
  @"\b(?:senior|sr\.?|principal|staff|lead|architect|сеньйор|сеньор|ведущий|провідний)\b",Flags);
 private static readonly Regex Hybrid = new(
  @"\bhybrid\b(?!\s+(?:cloud|architecture|systems?|storage|infrastructure|workloads?))|гібридн[а-яіїє]*\s+(?:формат|графік|робот|офіс|режим)|гибридн[а-я]*\s+(?:график|работ|офис|режим)|(?:\b[1-5]\b|\bone\b|\btwo\b|\bthree\b)\s*(?:days?|дні|дня|дней|днів|днi)\s*(?:per\s+week|a\s+week|на\s+тиждень|в\s+неделю)?\s*(?:в|у|in|at)?\s*(?:the\s+)?(?:офіс|офис|office)|(?:офіс|офис|office).{0,20}\b[1-5]\b\s*(?:days?|дн)|office\s+attendance\s+required",Flags);
 private static readonly Regex Onsite = new(
  @"\bon[- ]site\b|\bon\s+site\b|тільки\s+(?:в\s+)?офіс|только\s+(?:в\s+)?офис|office[- ]only|office\s+only|must\s+(?:work|be).{0,25}(?:in\s+)?(?:the\s+)?office|офісн(?:а|ий|ої)\s+робот|офисн(?:ая|ый)\s+работ",Flags);
 private static readonly Regex NoRemote = new(
  @"\bnot\s+(?:a\s+)?remote\b|\bno\s+remote\b(?!\s+(?:access|clients?|servers?|systems?|teams?))|remote\s+(?:work|option|positions?|roles?)\s+(?:is\s+|are\s+)?(?:not\s+available|not\s+allowed|not\s+possible|unavailable)|remote\s+(?:not\s+available|is\s+not\s+possible)|без\s+(?:можливості|возможности)\s+(?:віддален|удалён|удален)|віддален[а-яіїє]*\s+не\s+передбач|удал[её]н[а-я]*\s+не\s+предусмотр|(?:віддален|дистанційн|удал[её]н)[а-яіїє]*\s+(?:робот[а-яіїє]*|работ[а-я]*)\s+(?:неможлив|невозмож|не\s+(?:передбач|предусмотр))",Flags);
 private static readonly Regex Remote = new(
  @"\b(?:fully[- ]remote|remote[- ]first|remote[- ]only|remote\s+(?:position|role|work|job|available|within)|work\s+(?:fully\s+)?remotely|work\s+from\s+home|wfh)\b|(?<![a-z])remote(?![a-z])|віддален|дистанційн|дистанционн|удал[её]н|робот[а-яіїє]*\s+з\s+дому",Flags);

 private static readonly Regex ExplicitB2 = new(
  @"(?:english|англійськ[а-яіїє]*|английск[а-я]*).{0,42}\b(?:b2|c1|c2|upper[- ]intermediate|advanced)\b|\b(?:b2|c1|c2)\b.{0,28}(?:english|англійськ|английск)",Flags);
 private static readonly Regex SpokenEnglish = new(
  @"(?:spoken|speaking|oral|розмовн|разговорн).{0,35}(?:english|англійськ|английск)|(?:english|англійськ|английск).{0,35}(?:spoken|speaking|oral|розмовн|разговорн)",Flags);

 public static WorkMode DetectWorkMode(string title,string description)
 {
  var text=Normalize(title+" "+description);
  if(Hybrid.IsMatch(text))return WorkMode.Hybrid;
  if(NoRemote.IsMatch(text) || Onsite.IsMatch(text))return WorkMode.Onsite;
  // "Remote teams" and "remote access" alone do not establish a remote position.
  var withoutNoise=Regex.Replace(text,@"\bremote\s+(?:teams?|access|clients?|systems?|servers?)\b","",Flags);
  return Remote.IsMatch(withoutNoise)?WorkMode.Remote:WorkMode.Unknown;
 }

 public static VacancyAssessment Assess(VacancySnapshot job)
 {
  var title=Normalize(job.Title);
  var body=Normalize(job.Description.Length>0?job.Description:job.Preview??"");
  var all=title+" "+body;
  var reasons=new List<string>();
  var warnings=new List<string>();
  var mode=DetectWorkMode(title,body);
  var eligibility=VacancyEligibility.Analyze(title,body);
  warnings.AddRange(eligibility.Warnings);
  int score=0;

  if(Net.IsMatch(all)){score+=35;reasons.Add("C#/.NET указан в вакансии");}
  else warnings.Add("Не найдено явного требования C#/.NET");

  if(Middle.IsMatch(title)){score+=15;reasons.Add("Уровень Middle / strong Junior");}
  if(Regex.IsMatch(all,@"\b(?:backend|back[- ]end|web\s*api|rest\s*api|бекенд|бэкенд)\b",Flags))
   {score+=10;reasons.Add("Backend / API");}
  if(Regex.IsMatch(all,@"\b(?:asp\.net\s*core|web\s*api)\b",Flags))
   {score+=7;reasons.Add("ASP.NET Core / Web API");}
  if(Regex.IsMatch(all,@"\b(?:postgresql|postgres)\b",Flags))
   {score+=5;reasons.Add("PostgreSQL");}
  if(Regex.IsMatch(all,@"\b(?:ef\s*core|entity\s*framework)\b",Flags))
   {score+=5;reasons.Add("Entity Framework");}
  if(Regex.IsMatch(all,@"\b(?:rabbitmq|message\s+broker)\b",Flags))
   {score+=3;reasons.Add("RabbitMQ / messaging");}
  if(Regex.IsMatch(all,@"\b(?:fintech|trading|exchange\s+api|broker\s+api|алготрейдинг)\b",Flags))
   {score+=4;reasons.Add("Fintech / интеграции");}
  if(Regex.IsMatch(all,@"\bpython\b",Flags))
   {score+=2;reasons.Add("Python");}

  if(mode==WorkMode.Remote){score+=20;reasons.Add("Есть явное указание на удалённую работу");}
  if(mode==WorkMode.Hybrid)warnings.Add("Требуется присутствие в офисе / гибрид");
  if(mode==WorkMode.Onsite)warnings.Add("Явно указан офисный формат или запрет удалённой работы");
  if(mode==WorkMode.Unknown)warnings.Add("Удалённый формат не подтверждён");

  if(SeniorTitle.IsMatch(title))
  {
   if(Middle.IsMatch(title))warnings.Add("Смешанный уровень Middle/Senior — требуется проверка");
   else warnings.Add("Указан Senior/Lead/Architect — выше целевого уровня");
  }
  if(ExplicitB2.IsMatch(all))warnings.Add("Упомянут английский B2+ — нужно проверить обязательность");
  if(SpokenEnglish.IsMatch(all))warnings.Add("Упомянут разговорный английский");
  if(Regex.IsMatch(all,@"\b(?:wpf|kubernetes|aws|azure)\b",Flags))
   warnings.Add("Встречаются WPF / cloud / Kubernetes — проверить требования");
  if(!job.HasFullText)warnings.Add("Полный текст не получен: оценка только по анонсу");
  if(!job.HasFullText && !Net.IsMatch(all))warnings.Add("Короткий анонс без .NET не является доказательством нерелевантности");
  if(job.IsOpen==false)warnings.Add("Источник ранее явно обозначил вакансию закрытой");
  if(job.IsOpen is null)warnings.Add("Актуальность вакансии не подтверждена");

  var clearlySenior=SeniorTitle.IsMatch(title)&&!Middle.IsMatch(title);
  FitBucket tier;
  if(mode is WorkMode.Hybrid or WorkMode.Onsite || job.IsOpen==false
    || clearlySenior || (!Net.IsMatch(all) && job.HasFullText))
   tier=FitBucket.Excluded;
  else if(mode==WorkMode.Remote && job.HasFullText && score>=65 && !ExplicitB2.IsMatch(all)
    && !SpokenEnglish.IsMatch(all) && !SeniorTitle.IsMatch(title)
    && !eligibility.RequiresReview)
   tier=FitBucket.LikelyFit;
  else tier=FitBucket.NeedsReview;
  return new VacancyAssessment(job,mode,tier,score,reasons,warnings);
 }

 private static string Normalize(string input)=>
  Regex.Replace(System.Net.WebUtility.HtmlDecode(input),@"\s+"," ").Trim();
}
