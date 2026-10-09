using JobRadar;
using Xunit;
namespace JobRadar.Tests;
public sealed class RobotaCompanyTests
{
 [Fact]
 public void CompanyFeedYieldsFullTextAndPublishedDate()
 {
  var description="Build C# ASP.NET Core API services with PostgreSQL, RabbitMQ, Docker, CI/CD and .NET. "+
                  "Write specifications for Codex, verify AI generated code and enforce architecture and unit tests.";
  var json=System.Text.Json.JsonSerializer.Serialize(new {
    totalVacanciesCount=1,
    filteredVacancies=new[]{new {id=11287397,
      description="<p>"+description+"</p><script>remove</script>",
      date="2026-10-05T09:00:00Z"}}
  });
  var details=RobotaCompanyDetails.ParseCompanyFeed(json);
  var item=Assert.Single(details);
  Assert.Equal(11287397,item.Key);
  Assert.Contains("PostgreSQL",item.Value.Description);
  Assert.DoesNotContain("remove",item.Value.Description);
  Assert.Equal(new DateTimeOffset(2026,10,5,9,0,0,TimeSpan.Zero),item.Value.PublishedAt);
 }
 [Fact]
 public void MissingCompanyFeedArrayIsErrorNotEmptyResult()
 {
  Assert.Throws<InvalidDataException>(()=>RobotaCompanyDetails.ParseCompanyFeed("{}"));
 }
 [Fact]
 public void EmptyOrTruncatedDescriptionsAreNotAccepted()
 {
  const string json="""{"filteredVacancies":[{"id":77,"description":"short"}]}""";
  Assert.Empty(RobotaCompanyDetails.ParseCompanyFeed(json));
 }
}