using JobRadar;
using System.Text.Json;
using Xunit;

namespace JobRadar.Tests;

public sealed class TriageIntegrationTests
{
 [Fact]
 public async Task ExportKeepsRejectedRecordsInAuditButNotShortlist()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);
  var id=Guid.NewGuid().ToString("N");
  var src="triage-"+id;
  var a=new JobRef(src,$"https://jobs.dou.ua/vacancies/{id}-remote",
    "Middle .NET Backend",null,DateTimeOffset.UtcNow);
  var b=new JobRef(src,$"https://jobs.dou.ua/vacancies/{id}-hybrid",
    "Middle .NET Backend",null,DateTimeOffset.UtcNow);
  var uncertain=new JobRef(src,$"https://jobs.dou.ua/vacancies/{id}-unknown",
    "Middle .NET Backend",null,DateTimeOffset.UtcNow);
  var good="Fully remote ASP.NET Core REST API PostgreSQL EF Core. Пишемо українською. "+new string('A',100);
  var office="Remote also possible, but hybrid: 2 days in office. "+new string('B',100);
  foreach(var j in new[]{a,b,uncertain})await store.SaveDiscovered(j,CancellationToken.None,"C#");
  await store.Save(new JobDetail(a,good,null,DateTimeOffset.UtcNow),CancellationToken.None);
  await store.Save(new JobDetail(b,office,null,DateTimeOffset.UtcNow),CancellationToken.None);
  await store.Save(new JobDetail(uncertain,"ASP.NET Core PostgreSQL EF Core "+new string('C',100),null,DateTimeOffset.UtcNow),CancellationToken.None);
  var db=await store.ReadVacanciesForTriageAsync(50000,CancellationToken.None);
  var goodOne=VacancyTriage.Assess(Assert.Single(db,x=>x.Url==a.Url));
  var badOne=VacancyTriage.Assess(Assert.Single(db,x=>x.Url==b.Url));
  Assert.Equal(FitBucket.LikelyFit,goodOne.Bucket);
  Assert.Equal(FitBucket.Excluded,badOne.Bucket);
  Assert.Contains("C#",goodOne.Vacancy.Queries);

  var folder=Path.Combine(Path.GetTempPath(),"jobradar-test-"+id);
  try
  {
   var result=await TriageExport.WriteAsync(store,folder,50000,CancellationToken.None);
   Assert.Equal(result.Evaluated,result.LikelyFit+result.NeedsReview+result.Excluded);
   Assert.True(result.TotalStored>=result.Evaluated);
   Assert.Equal(Math.Max(0L,result.TotalStored-result.Evaluated),result.OmittedByLimit);
   Assert.True(result.HighScoreNeedsReview>=0);
   Assert.True(result.IncompleteTextNeedsReview>=0);
   Assert.True(result.GeoRestrictedNeedsReview>=0);
   Assert.True(result.UnverifiedOpenStatus>=3);
   var all=await File.ReadAllLinesAsync(result.AuditFile);
   var selected=await File.ReadAllLinesAsync(result.ShortlistFile);
   var review=await File.ReadAllLinesAsync(result.ReviewFile);
   var compact=await File.ReadAllLinesAsync(result.CompactFile);
   Assert.Equal(all.Length,compact.Length);
   Assert.Contains(all,line=>line.Contains("Пишемо українською.",StringComparison.Ordinal));
   Assert.DoesNotContain(all,line=>line.Contains("\\u041f",StringComparison.OrdinalIgnoreCase));
   Assert.Contains(all,line=>line.Contains(a.Url,StringComparison.Ordinal));
   Assert.Contains(all,line=>line.Contains(b.Url,StringComparison.Ordinal));
   Assert.Contains(selected,line=>line.Contains(a.Url,StringComparison.Ordinal));
   Assert.DoesNotContain(selected,line=>line.Contains(b.Url,StringComparison.Ordinal));
   Assert.DoesNotContain(selected,line=>line.Contains(uncertain.Url,StringComparison.Ordinal));
   Assert.Contains(review,line=>line.Contains(uncertain.Url,StringComparison.Ordinal));
   using var compactJson=JsonDocument.Parse(Assert.Single(compact.Where(line=>line.Contains(a.Url))));
   Assert.Equal("LikelyFit",compactJson.RootElement.GetProperty("Bucket").GetString());
   Assert.False(compactJson.RootElement.TryGetProperty("Description",out _));
   Assert.False(compactJson.RootElement.TryGetProperty("Vacancy",out _));
   Assert.Contains("Пишемо українською.",selected.Single(line=>line.Contains(a.Url)));
   using var json=JsonDocument.Parse(selected.Single(line=>line.Contains(a.Url)));
   Assert.Equal("LikelyFit",json.RootElement.GetProperty("Bucket").GetString());
  }
  finally
  {
   if(Directory.Exists(folder))Directory.Delete(folder,true);
  }
 }
}
