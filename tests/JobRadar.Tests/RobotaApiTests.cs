using JobRadar;
using Xunit;
namespace JobRadar.Tests;
public sealed class RobotaApiTests
{
 [Fact]
 public void ReadsStructuredVacanciesAndReportedTotal()
 {
  const string json="""
   {"total":51,"documents":[
    {"id":11287397,"notebookId":307419,"name":"Backend Engineer (.NET, Agentic Engineering)","companyName":"КРЕДІ АГРІКОЛЬ БАНК","date":"2026-10-05T09:00:00Z","shortDescription":"Build agentic systems"},
    {"id":11287397,"notebookId":307419,"name":"Backend Engineer (.NET, Agentic Engineering)","companyName":"КРЕДІ АГРІКОЛЬ БАНК"},
    {"id":432,"notebookId":null,"name":"Invalid missing company"}]}
   """;
  IJobSource source=new RobotaApiSource(".net");
  Assert.Equal(51,source.ReportedTotal(json));
  var vacancy=Assert.Single(source.ParseListings(json));
  Assert.Equal("https://robota.ua/company307419/vacancy11287397",vacancy.Url);
  Assert.Equal("КРЕДІ АГРІКОЛЬ БАНК",vacancy.Company);
  Assert.Equal(new DateTimeOffset(2026,10,5,9,0,0,TimeSpan.Zero),vacancy.PublishedAt);
 }
 [Fact]
 public void UsesZeroBasedPaginationAsLegacyApiDoes()
 {
  var source=new RobotaApiSource("C#");
  Assert.Contains("page=0",source.ListingUrl(0));
  Assert.Contains("page=1",source.ListingUrl(1));
  Assert.Contains("keyWords=C%23",source.ListingUrl(0));
 }
 [Fact]
 public void MissingDocumentsIsAnErrorNotZeroVacancies()
 {
  var source=new RobotaApiSource("backend");
  Assert.Throws<InvalidDataException>(()=>source.ParseListings("""{"total":0}"""));
 }
}
