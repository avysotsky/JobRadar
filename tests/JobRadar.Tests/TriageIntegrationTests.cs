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
  var good="Fully remote ASP.NET Core REST API PostgreSQL EF Core. "+new string('A',100);
  var office="Remote also possible, but hybrid: 2 days in office. "+new string('B',100);
  foreach(var j in new[]{a,b})await store.SaveDiscovered(j,CancellationToken.None,"C#");
  await store.Save(new JobDetail(a,good,null,DateTimeOffset.UtcNow),CancellationToken.None);
  await store.Save(new JobDetail(b,office,null,DateTimeOffset.UtcNow),CancellationToken.None);
  var db=await store.ReadVacanciesForTriageAsync(50000,CancellationToken.None);
  var goodOne=VacancyTriage.Assess(Assert.Single(db.Where(x=>x.Url==a.Url)));
  var badOne=VacancyTriage.Assess(Assert.Single(db.Where(x=>x.Url==b.Url)));
  Assert.Equal(FitBucket.LikelyFit,goodOne.Bucket);
  Assert.Equal(FitBucket.Excluded,badOne.Bucket);
  Assert.Contains("C#",goodOne.Vacancy.Queries);

  var folder=Path.Combine(Path.GetTempPath(),"jobradar-test-"+id);
  try
  {
   var result=await TriageExport.WriteAsync(store,folder,50000,CancellationToken.None);
   Assert.Equal(result.Evaluated,result.LikelyFit+result.NeedsReview+result.Excluded);
   var all=await File.ReadAllLinesAsync(result.AuditFile);
   var selected=await File.ReadAllLinesAsync(result.ShortlistFile);
   Assert.Contains(all,line=>line.Contains(a.Url,StringComparison.Ordinal));
   Assert.Contains(all,line=>line.Contains(b.Url,StringComparison.Ordinal));
   Assert.Contains(selected,line=>line.Contains(a.Url,StringComparison.Ordinal));
   Assert.DoesNotContain(selected,line=>line.Contains(b.Url,StringComparison.Ordinal));
   using var json=JsonDocument.Parse(selected.Single(line=>line.Contains(a.Url)));
   Assert.Equal("LikelyFit",json.RootElement.GetProperty("Bucket").GetString());
  }
  finally
  {
   if(Directory.Exists(folder))Directory.Delete(folder,true);
  }
 }
}
