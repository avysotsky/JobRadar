using System.Text;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class UpworkProjectsTests
{
 private const string JobUrl="https://www.upwork.com/jobs/~022108958663146489808";
 private static string Row(
  string title="C# .NET REST API Broker Integration",
  string? url=null,DateTimeOffset? captured=null,string? desc=null,
  string? experience="intermediate",bool? applied=false)=>
  JsonSerializer.Serialize(new
  {
   url=url??JobUrl,title,
   description_snippet=desc??"ASP.NET Core webhook and trading exchange API integration",
   skills=new[]{"C#","ASP.NET Core","API Integration"},
   job_type="fixed",budget="500.00",
   experience_level=experience,published_date="2026-10-10T16:31:47Z",
   captured_at=captured??DateTimeOffset.UtcNow,proposals_tier="5 to 10",
   client_country="United States",applied
  });

 [Fact]
 public void CanonicalizesOfficialUrlsAndRemovesTracking()
 {
  var input="https://upwork.com/jobs/~022108958663146489808?utm_source=chatgpt#details";
  Assert.Equal(JobUrl,UpworkProjects.CanonicalUrl(input));
 }

 [Theory]
 [InlineData("http://www.upwork.com/jobs/~022108958663146489808")]
 [InlineData("https://www.upwork.com.evil.test/jobs/~022108958663146489808")]
 [InlineData("https://user:pass@www.upwork.com/jobs/~022108958663146489808")]
 [InlineData("https://www.upwork.com:8443/jobs/~022108958663146489808")]
 [InlineData("https://www.upwork.com/jobs/")]
 [InlineData("https://www.upwork.com/jobs/~02something")]
 [InlineData("https://freelancehunt.com/project/1234")]
 [InlineData("https://www.upwork.com/jobs/~022108958663146489808%2fother")]
 public void RejectsUntrustedUrls(string url)=>
  Assert.Null(UpworkProjects.CanonicalUrl(url));

 [Fact]
 public void ExistingFreelanceScorerIsReused()
 {
  Assert.True(UpworkProjects.TryAssess(Row(),DateTimeOffset.UtcNow,out var item));
  Assert.NotNull(item);
  Assert.Equal("upwork",item.Source);
  Assert.Equal("Priority",item.Bucket);
  Assert.True(item.Score>=55);
  Assert.Contains(item.Reasons,x=>x.Contains("C#/.NET"));
  Assert.Equal("500.00",item.BudgetLabel);
  Assert.Contains(item.Warnings,x=>x.Contains("Connects"));
  Assert.DoesNotContain("exchange API integration",JsonlOutput.Serialize(item));
 }

 [Theory]
 [InlineData("expert",true)]
 [InlineData("intermediate",true)]
 public void AlreadyAppliedAndExpertRequireReview(string level,bool applied)
 {
  Assert.True(UpworkProjects.TryAssess(
   Row(experience:level,applied:applied),DateTimeOffset.UtcNow,out var item));
  Assert.Equal("Review",item!.Bucket);
  Assert.Contains(item.Warnings,x=>x.Contains("Already applied"));
 }

 [Fact]
 public void ExpertOrSeniorNeverBecomesAutomaticPriority()
 {
  Assert.True(UpworkProjects.TryAssess(
   Row(title:"Senior .NET Architect",experience:"expert",applied:false),
   DateTimeOffset.UtcNow,out var item));
  Assert.Equal("Review",item!.Bucket);
 }

 [Fact]
 public void RejectsStaleOrFutureData()
 {
  var now=DateTimeOffset.UtcNow;
  Assert.False(UpworkProjects.TryAssess(
   Row(captured:now-TimeSpan.FromHours(25)),now,out _));
  Assert.False(UpworkProjects.TryAssess(
   Row(captured:now+TimeSpan.FromHours(1)),now,out _));
  Assert.True(UpworkProjects.TryAssess(
   Row(captured:now-TimeSpan.FromHours(1)),now,out _));
 }

 [Fact]
 public void RejectsUntrustedFieldLengthsAndMalformedJson()
 {
  Assert.False(UpworkProjects.TryAssess(
   Row(title:new string('x',500)),DateTimeOffset.UtcNow,out _));
  Assert.False(UpworkProjects.TryAssess(
   "{invalid-json",DateTimeOffset.UtcNow,out _));
  Assert.False(UpworkProjects.TryAssess(
   "{\"url\":\"https://www.upwork.com/jobs/~022108958663146489808\",\"title\":\"No timestamp\"}",
   DateTimeOffset.UtcNow,out _));
 }

 [Fact]
 public async Task ReviewFileHasNoProviderApiOrDatabaseWrites()
 {
  var folder=Path.Combine(Path.GetTempPath(),"jobradar-upwork-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(folder);
  var path=Path.Combine(folder,"upwork.jsonl");
  try
  {
   await File.WriteAllTextAsync(path,
    Row()+"\n"+Row(url:JobUrl+"?utm_source=test")+"\n"+Row(title:"another",url:"https://evil.test/jobs/123")+"\n",
    new UTF8Encoding(false,true));
   using var output=new StringWriter();
   var report=await UpworkProjects.ReviewFileAsync(path,output,CancellationToken.None);
   Assert.Equal(3,report.LinesRead);
   Assert.Equal(1,report.Accepted);
   Assert.Equal(1,report.Rejected);
   Assert.Equal(1,report.Duplicates);
   Assert.Equal(1,report.Priority);
   var text=output.ToString();
   var line=Assert.Single(text.Split('\n',StringSplitOptions.RemoveEmptyEntries));
   using var doc=JsonDocument.Parse(line);
   Assert.Equal("upwork",doc.RootElement.GetProperty("Source").GetString());
   Assert.False(doc.RootElement.TryGetProperty("DescriptionSnippet",out _));
  }
  finally {Directory.Delete(folder,true);}
 }

 [Fact]
 public async Task UpworkRowsMergeWithEmploymentWithoutSavingProviderDescriptions()
 {
  var files=new Dictionary<OpportunityInputKind,TextReader>
  {
   [OpportunityInputKind.Upwork]=new StringReader(Row()+"\n")
  };
  using var output=new StringWriter();
  var report=await OpportunityPreview.WriteAsync([],
   new Dictionary<(string,string),JobDiscoveryHistory>(),
   [],files,output,CancellationToken.None);
  Assert.Equal(1,report.TotalUnique);
  Assert.Equal(1,report.FreelanceProjects);
  Assert.Equal(1,report.FreelancePriority);
  Assert.Equal("SNAPSHOT_ONLY_UNVERIFIED_COVERAGE",report.Inputs[0].CoverageStatus);
  var serialized=output.ToString();
  Assert.DoesNotContain("ASP.NET Core webhook",serialized);
  using var doc=JsonDocument.Parse(serialized);
  var root=doc.RootElement;
  Assert.Equal("upwork",root.GetProperty("Source").GetString());
  Assert.Equal("FreelanceProject",root.GetProperty("Kind").GetString());
  Assert.Equal("OPEN_STATUS_UNVERIFIED",
    root.GetProperty("StatusEvidence").GetString());
  Assert.Equal("500.00",root.GetProperty("BudgetLabel").GetString());
 }

 [Fact]
 public async Task UnifiedPreviewRejectsUpworkRecordCountAboveMaximum()
 {
  // In-process imports may not rely on the Program.cs FileInfo guard.
  var file=new Dictionary<OpportunityInputKind,TextReader>
  {
   [OpportunityInputKind.Upwork]=new StringReader(
    string.Concat(Enumerable.Repeat(Row()+"\n",UpworkProjects.MaxRows+1)))
  };
  using var output=new StringWriter();
  await Assert.ThrowsAsync<InvalidDataException>(()=>
   OpportunityPreview.WriteAsync([],
    new Dictionary<(string,string),JobDiscoveryHistory>(),
    [],file,output,CancellationToken.None));
 }

 [Fact]
 public void RealConnectorSnapshotShapeWithoutAppliedFieldIsUnverified()
 {
  var json=System.Text.Json.JsonSerializer.Serialize(new
  {
   url=JobUrl,
   title="Two-Way Auto Sync: ASP.NET Core MVC Sales Portal ↔ Microsoft Dynamics 365 CRM",
   description_snippet="ASP.NET Core MVC ↔ CRM API integration",
   skills=new[]{"ASP.NET MVC","C#","API Integration"},
   job_type="fixed",budget="50.00",experience_level="intermediate",
   published_date="2026-10-10T16:31:47.175Z",
   captured_at=DateTimeOffset.UtcNow,proposals_tier="20 to 50",
   client_country="United States"
  });
  Assert.True(UpworkProjects.TryAssess(json,DateTimeOffset.UtcNow,out var candidate));
  Assert.NotNull(candidate);
  Assert.Null(candidate.Applied);
  Assert.Equal("upwork",candidate.Source);
  Assert.Equal(JobUrl,candidate.Url);
  Assert.Contains(candidate.Warnings,x=>x.Contains("Connects"));
 }

 [Fact]
 public async Task ReviewFileFailsClosedForUnboundedInputs()
 {
  var path=Path.Combine(Path.GetTempPath(),"jobradar-upwork-"+Guid.NewGuid()+".jsonl");
  try
  {
   await File.WriteAllTextAsync(path,new string('x',2_000_001));
   using var output=new StringWriter();
   await Assert.ThrowsAsync<InvalidDataException>(()=>
    UpworkProjects.ReviewFileAsync(path,output,CancellationToken.None));
   Assert.Empty(output.ToString());
  }
  finally {if(File.Exists(path))File.Delete(path);}
 }
}
