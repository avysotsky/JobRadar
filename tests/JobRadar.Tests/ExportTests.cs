using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;
public sealed class ExportTests
{
 [Fact]public async Task WritesFullDescriptionsAndProvenanceWithExplicitCompletenessFlag()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);await storage.Initialize(CancellationToken.None);
  string unique="export-"+Guid.NewGuid().ToString("N");
  var job=new JobRef(unique,"https://jobs.dou.ua/vacancies/523111/",
    "Middle Backend C#", "Example",DateTimeOffset.UtcNow,"preview only");
  await storage.SaveDiscovered(job,CancellationToken.None,"dou-rss-C#");
  await storage.Save(new JobDetail(job,new string('X',145),null,DateTimeOffset.UtcNow),CancellationToken.None);
  using var sw=new StringWriter();
  var rows=await storage.ExportJobsJsonlAsync(sw,5000,CancellationToken.None);
  Assert.True(rows>=1);
  var matching=sw.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries)
   .Select(line=>JsonDocument.Parse(line))
   .ToList();
  try
  {
   var found=matching.Single(x=>x.RootElement.GetProperty("Source").GetString()==unique);
   Assert.True(found.RootElement.GetProperty("HasFullText").GetBoolean());
   Assert.Equal(145,found.RootElement.GetProperty("Description").GetString()!.Length);
   Assert.Contains("dou-rss-C#",found.RootElement.GetProperty("Queries").EnumerateArray().Select(x=>x.GetString()));
  }
  finally{foreach(var d in matching)d.Dispose();}
 }
 [Fact]public async Task RejectsUnboundedExport()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
    ()=>new Storage(cs).ExportJobsJsonlAsync(new StringWriter(),50001,CancellationToken.None));
 }
}
