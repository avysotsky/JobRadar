using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class EmploymentDecisionStorageTests
{
 [Fact]
 public async Task OwnerAppliedOrInactiveOverlaysShareCanonicalUrlWithoutChangingProviderStatus()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var id=Guid.NewGuid().ToString("N");
  var url="https://jobs.dou.ua/vacancies/"+id+"/";
  await storage.MarkEmploymentDecisionAsync(url,"Applied",CancellationToken.None);
  var first=await storage.ReadEmploymentDecisionsAsync(CancellationToken.None);
  Assert.Equal("Applied",first[Storage.CanonicalEmploymentUrl(url)].Decision);

  await storage.MarkEmploymentDecisionAsync(url.TrimEnd('/'),"Inactive",CancellationToken.None);
  var second=await storage.ReadEmploymentDecisionsAsync(CancellationToken.None);
  var decision=second[Storage.CanonicalEmploymentUrl(url)];
  Assert.Equal("Inactive",decision.Decision);
  Assert.NotEqual(default,decision.UpdatedAt);
  Assert.Equal(Storage.CanonicalEmploymentUrl(url),decision.Url);
 }

 [Theory]
 [InlineData("Appliedx")]
 [InlineData("Open")]
 [InlineData("Closed")]
 public async Task UnsupportedProviderClaimsAreRejectedBeforeDatabase(string invalid)
 {
  var storage=new Storage("");
  await Assert.ThrowsAsync<ArgumentException>(()=>
   storage.MarkEmploymentDecisionAsync(
    "https://jobs.dou.ua/vacancies/123",invalid,CancellationToken.None));
 }
}
