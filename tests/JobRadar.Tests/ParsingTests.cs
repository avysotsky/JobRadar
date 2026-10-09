using JobRadar;
using Xunit;
namespace JobRadar.Tests;
public sealed class ParsingTests
{
 [Fact]public void DouListingExtractsVacancy(){var html="<a class='vt' href='/vacancies/1234/'>Middle .NET</a>";var jobs=Parsers.Listings("dou",html,new Uri("https://jobs.dou.ua/"));Assert.Single(jobs);Assert.Equal("https://jobs.dou.ua/vacancies/1234/",jobs[0].Url);}
 [Fact]public void DjinniListingExtractsVacancy(){var html="<a href='/jobs/12345-middle-net/'>Middle C# developer</a>";Assert.Single(Parsers.Listings("djinni",html,new Uri("https://djinni.co/")));}
 [Fact]public void JobPostingDescriptionFromJsonLd(){var html="""<script type="application/ld+json">{"@type":"JobPosting","description":"<p>Build REST APIs and test them.</p>"}</script>""";Assert.Equal("Build REST APIs and test them.",Parsers.Description("djinni",html));}
 [Fact]public void ClosedVacancyFlag(){Assert.False(Parsers.OpenStatus("<html><body>Ця вакансія вже завершена</body></html>"));}
 [Fact]public void ApplyButtonDoesNotProveActive(){Assert.Null(Parsers.OpenStatus("<button>Відгукнутися</button>"));}
}