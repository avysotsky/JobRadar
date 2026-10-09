using System.Net;
using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>
/// Conservative, multilingual eligibility signals. These are review gates, not proof
/// of legal eligibility or employer policy. Only explicit restrictions count.
/// </summary>
public sealed record EligibilitySignals(
 bool LocationRestricted,
 bool MandatoryRelocation,
 bool BackendEvidence,
 bool DesktopCore,
 IReadOnlyList<string> Warnings)
{
 public bool RequiresReview => LocationRestricted || MandatoryRelocation || DesktopCore || !BackendEvidence;
}

public static class VacancyEligibility
{
 private const RegexOptions Flags = RegexOptions.Compiled |
  RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

 private static readonly Regex Backend = new(
  @"\b(?:backend|back[- ]end|back[- ]end\s+services?|server[- ]side|web\s*api|rest\s*api|api\s+development|мікросервіс|микросервис|бекенд|бэкенд|серверн[а-яіїє]*\s+(?:частин|част|розроб|разработ))\b",
  Flags);

 // The location must be a stated eligibility restriction, not a mere office location
 // or a reference to an EU time zone. Ukraine is deliberately not in RestrictedPlaces.
 private const string RestrictedPlaces =
  @"(?:poland|germany|czechia|czech\s+republic|romania|spain|france|netherlands|"
  + @"united\s+kingdom|uk|united\s+states|usa|us|canada|european\s+union|eu|"
  + @"польщ[а-яіїє]*|польш[а-я]*|німеччин[а-яіїє]*|германи[а-я]*|"
  + @"єс|ес|великобритан[а-яіїє]*|британ[а-яіїє]*)";

 private static readonly Regex LimitedRemote = new(
  @"(?:\bremote\b.{0,35}\b(?:only|exclusively)\b.{0,20}\b" + RestrictedPlaces + @"\b)"
  + @"|(?:\bremote\b\s+(?:only\s+)?(?:within|from|in)\s+(?:the\s+)?\b" + RestrictedPlaces + @"\b)"
  + @"|(?:\b(?:must|required\s+to|need\s+to)\s+(?:be\s+)?(?:based|located|resident|reside)\s+(?:in|within)\s+(?:the\s+)?\b" + RestrictedPlaces + @"\b)"
  + @"|(?:\b" + RestrictedPlaces + @"\s+(?:residents?|candidates?)\s+only\b)"
  + @"|(?:\b(?:excluding|except|not\s+available\s+in|not\s+eligible\s+in)\s+ukraine\b)"
  + @"|(?:не\s+(?:розглядаємо|рассматриваемо?|працюємо|работаем)\s+.{0,25}(?:україн|украин))",
  Flags);

 private static readonly Regex Relocation = new(
  @"\b(?:relocation\s+(?:is\s+)?(?:required|mandatory)|"
  + @"(?:must|need\s+to|required\s+to)\s+relocate|"
  + @"requires?\s+relocation|relocate\s+to)\b"
  + @"|(?:обов['’]язков[а-яіїє]*\s+(?:переїзд|релокац)|"
  + @"(?:переїзд|релокац[а-яіїє]*)\s+обов['’]язков[а-яіїє]*|"
  + @"обязательн[а-я]*\s+(?:переезд|релокац)|"
  + @"(?:переезд|релокац[а-я]*)\s+обязател[а-я]*)",
  Flags);

 private static readonly Regex DesktopTitle = new(
  @"\b(?:wpf|winforms|windows\s+forms|desktop(?:\s+application)?\s+developer)\b",
  Flags);
 private static readonly Regex DesktopMandatory = new(
  @"\b(?:mandatory|required|must\s+have|essential|обов['’]язков[а-яіїє]*|"
  + @"обязательн[а-я]*)\b.{0,35}\b(?:wpf|winforms)\b"
  + @"|\b(?:wpf|winforms)\b.{0,20}\b(?:required|mandatory|обов['’]язков[а-яіїє]*|обязательн[а-я]*)\b",
  Flags);

 public static EligibilitySignals Analyze(string title,string description)
 {
  var normalizedTitle=Normalize(title);
  var text=Normalize(title+" "+description);
  var warnings=new List<string>();
  bool location=LimitedRemote.IsMatch(text);
  bool relocation=Relocation.IsMatch(text);
  bool backend=Backend.IsMatch(text);
  bool desktop=DesktopTitle.IsMatch(normalizedTitle) || DesktopMandatory.IsMatch(text);

  if(location)warnings.Add("Есть ограничение страны проживания/работы: доступность из Украины требует проверки");
  if(relocation)warnings.Add("Указана обязательная релокация: удалённость из Украины требует проверки");
  if(!backend)warnings.Add("Backend/API обязанности не подтверждены явно");
  if(desktop)warnings.Add("Возможна обязательная WPF/WinForms или desktop-специализация");

  return new EligibilitySignals(location,relocation,backend,desktop,warnings);
 }

 private static string Normalize(string value) =>
  Regex.Replace(WebUtility.HtmlDecode(value),@"\s+"," ").Trim();
}
