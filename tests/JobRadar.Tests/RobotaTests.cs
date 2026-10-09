using JobRadar;
using Xunit;
namespace JobRadar.Tests;

public sealed class RobotaTests
{
 [Fact]
 public void SearchPageExtractsVacancyCardsAndDeduplicates()
 {
  const string html = """
    <html><body><h1>Робота .net</h1><p>51 вакансія</p>
    <a href="/company307419/vacancy11287397"><h3>Backend Engineer (.NET, Agentic Engineering)</h3></a>
    <a href="/company307419/vacancy11287397"><h3>Duplicate</h3></a>
    <a href="https://evil.example/company123/vacancy999"><h3>External</h3></a>
    <a href="/company307419"><h3>Company page</h3></a></body></html>
    """;
  var jobs = new RobotaSource(".net").ParseListings(html);
  var job = Assert.Single(jobs);
  Assert.Equal("Backend Engineer (.NET, Agentic Engineering)", job.Title);
  Assert.Equal("https://robota.ua/company307419/vacancy11287397", job.Url);
  Assert.Equal(51, Parsers.ReportedTotal(html));
 }
 [Fact]
 public void DetailParsesPublicationDateAndCompleteDescription()
 {
  const string html = """
    <main><h1>Backend Engineer (.NET, Agentic Engineering)</h1>
    <div>05 жовтня 2026</div>
    <div class="vacancy-description">
    <p>Ваша роль — направляти AI-агентів, створювати специфікації, розробляти сервіси на C# та ASP.NET Core.</p>
    <p>Для успішної роботи: PostgreSQL, Docker, RabbitMQ, API, EF Core. Також два дні в офісі.</p></div>
    <h2>Схожі вакансії</h2><a href="/company4/vacancy5">Unrelated</a>
    </main>
    """;
  var description = Parsers.Description("robota-.net", html);
  Assert.Contains("PostgreSQL", description);
  Assert.DoesNotContain("Unrelated", description);
  Assert.Equal(new DateTimeOffset(2026,10,5,0,0,0,TimeSpan.Zero), RobotaParser.PublishedDate(html));
 }
 [Fact]
 public void UnknownDetailIsNotAcceptedAsFullText()
 {
  Assert.Equal("",Parsers.Description("robota-.net","<html><body>Captcha or empty response</body></html>"));
 }
 [Fact]
 public void SearchPageHasExplicitPageNumber()
 {
  var source=new RobotaSource(".net");
  Assert.Equal("https://robota.ua/zapros/.net/ukraine",source.ListingUrl(0));
  Assert.EndsWith("page=2",source.ListingUrl(1));
 }
}