using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class ReviewExportOrderingTests
{
 [Fact]
 public async Task JsonlReviewQueuePlacesTargetBackendBeforePartialAndRestrictedJobs()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);

  var id=Guid.NewGuid().ToString("N");
  var source="phase31-"+id;
  JobRef Make(string suffix,string title)=>new(source,
   "https://jobs.dou.ua/vacancies/"+id+"-"+suffix,title,null,
   DateTimeOffset.UtcNow);

  var target=Make("backend","Middle .NET Backend Developer");
  var partial=Make("partial","Middle .NET Developer");
  var restricted=Make("restricted","Middle .NET Backend Developer");
  var desktop=Make("desktop","Middle .NET WPF Developer");
  var data=new[]
  {
   (target,"Fully remote ASP.NET Core REST API EF Core PostgreSQL "+new string('A',100)),
   (partial,"Fully remote work with C# and .NET internal tooling "+new string('B',100)),
   (restricted,"Remote only USA. ASP.NET Core REST API EF Core PostgreSQL "+new string('C',100)),
   (desktop,"Fully remote ASP.NET Core REST API EF Core PostgreSQL "+new string('D',100))
  };
  foreach(var (job,body) in data)
  {
   await store.SaveDiscovered(job,CancellationToken.None,"C#");
   await store.Save(new JobDetail(job,body,null,DateTimeOffset.UtcNow),CancellationToken.None);
  }

  var folder=Path.Combine(Path.GetTempPath(),"jobradar-phase31-"+id);
  try
  {
   var exported=await TriageExport.WriteAsync(store,folder,50000,CancellationToken.None);
   var lines=await File.ReadAllLinesAsync(exported.ReviewFile);
   var urls=new List<string>();
   foreach(var line in lines)
   {
    using var doc=JsonDocument.Parse(line);
    urls.Add(doc.RootElement.GetProperty("Vacancy").GetProperty("Url").GetString()!);
   }
   int Index(JobRef job)=>urls.FindIndex(url=>
    string.Equals(url,job.Url,StringComparison.Ordinal));
   var targetIndex=Index(target);
   var partialIndex=Index(partial);
   var restrictedIndex=Index(restricted);
   var desktopIndex=Index(desktop);
   Assert.True(targetIndex>=0 && partialIndex>=0 &&
    restrictedIndex>=0 && desktopIndex>=0,
    "All four unverified vacancies must remain in NeedsReview");
   Assert.True(targetIndex<partialIndex);
   Assert.True(partialIndex<restrictedIndex);
   Assert.True(restrictedIndex<desktopIndex);

   var shortlist=await File.ReadAllTextAsync(exported.ShortlistFile);
   Assert.DoesNotContain(target.Url,shortlist,StringComparison.Ordinal);
   Assert.DoesNotContain(restricted.Url,shortlist,StringComparison.Ordinal);
  }
  finally
  {
   if(Directory.Exists(folder))Directory.Delete(folder,true);
  }
 }
}
