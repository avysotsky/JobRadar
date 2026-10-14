using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class CrossBoardDuplicateStorageTests
{
 [Fact]
 public async Task SeparateSavedProviderUrlsReportNearIdenticalDescriptionsWithoutTransferringApplied()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var id=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+
    Random.Shared.Next(1000,9999);
  var dou=new JobRef("dou",
   $"https://jobs.dou.ua/companies/strimco/vacancies/{id}/",
   "Middle .NET Developer в Strimco, Київ, віддалено",
   "Strimco",DateTimeOffset.UtcNow);
  var djinni=new JobRef("djinni",
   $"https://djinni.co/jobs/{id}-middle-net-developer/",
   "Middle .NET Developer",null,DateTimeOffset.UtcNow);
  var repeated=string.Join(" ",Enumerable.Range(1,28).Select(i=>
   $"Державні цифрові сервіси номер {i} на ASP.NET Core, RabbitMQ, "+
   "PostgreSQL, EF Core, REST API з надійними інтеграціями."));
  await storage.SaveDiscovered(dou,CancellationToken.None,"dou");
  await storage.SaveDiscovered(djinni,CancellationToken.None,"djinni");
  await storage.Save(new JobDetail(dou,repeated,null,DateTimeOffset.UtcNow),
   CancellationToken.None);
  await storage.Save(new JobDetail(djinni,
   repeated.Replace(". ",".\n"),null,DateTimeOffset.UtcNow),
   CancellationToken.None);
  await storage.MarkEmploymentDecisionAsync(dou.Url,"Applied",CancellationToken.None);
  var saved=(await storage.ReadVacanciesForTriageAsync(
   50000,CancellationToken.None))
   .Where(x=>x.Url==dou.Url || x.Url==djinni.Url).ToArray();
  Assert.Equal(2,saved.Length);
  Assert.All(saved,x=>Assert.True(x.HasFullText));
  var own=await storage.ReadEmploymentDecisionsAsync(CancellationToken.None);
  var report=CrossBoardDuplicateAudit.Analyze(
   saved,own,saved.Length,DateTimeOffset.UtcNow);
  var pair=Assert.Single(report.Pairs);
  Assert.True(pair.TextSimilarity>=0.96);
  Assert.Equal("Applied",pair.First.OwnerDecision);
  Assert.Null(pair.Second.OwnerDecision);
  Assert.Equal("Strimco",pair.First.Company);
  Assert.Null(pair.Second.Company);
 }
}
