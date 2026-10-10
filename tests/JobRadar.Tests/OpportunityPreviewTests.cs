using System.Text.Json;
using JobRadar;
using Npgsql;
using Xunit;

namespace JobRadar.Tests;

public sealed class OpportunityPreviewTests
{
 private static VacancySnapshot Employment(string source="dou",string? url=null)=>
  new(source,url??"https://jobs.dou.ua/vacancies/991123/",
   "Middle .NET Backend Engineer","Example Company",null,
   "Fully remote ASP.NET Core REST API PostgreSQL EF Core RabbitMQ. Ukrainian language.",
   true,null,new DateTimeOffset(2026,10,10,9,0,0,TimeSpan.Zero),
   new DateTimeOffset(2026,10,10,12,0,0,TimeSpan.Zero),["C#"]);

 private static FreelancehuntCandidate Hunt(string? url=null,bool plus=false)
 {
  var project=new FreelancehuntProject(221,
   url??"https://freelancehunt.com/project/trading-api/221.html",
   "C# trading broker API integration",
   "Implement REST API for trading execution with .NET.",
   3000m,"UAH",["C#","API"],3,plus,false,
   DateTimeOffset.UtcNow,"Open");
  return FreelancehuntProjects.Rank(project);
 }

 private static FreelancerCandidate Freelancer(string? url=null)
 {
  var project=new FreelancerProject(101,
   url??"https://www.freelancer.com/projects/trading-backend-101",
   "C# trading execution API",
   "ASP.NET Core broker exchange API integration for crypto.",
   "USD",120m,400m,"FIXED",["C#",".NET"]);
  return FreelancerProjects.Rank(project,["C#","broker"]);
 }

 private static RemoteBoardCandidate Wwr(string source="wwr-backend")
 {
  var opening=new RemoteBoardOpening(source,
   "https://weworkremotely.com/remote-jobs/example-middle-net",
   "Middle .NET Backend",null,"Fully remote ASP.NET Core Web API",
   DateTimeOffset.UtcNow,null,null,null,false,"We Work Remotely");
  return new RemoteBoardCandidate(opening,FitBucket.NeedsReview,80,[".NET"],["Excerpt only"]);
 }

 [Fact]
 public async Task EmptyOptionalInputsPreserveStoredEmploymentAndUnverifiedStatus()
 {
  var job=Employment();
  using var writer=new StringWriter();
  var result=await OpportunityPreview.WriteAsync([job],
   new Dictionary<(string,string),JobDiscoveryHistory>(),
   [],new Dictionary<OpportunityInputKind,TextReader>(),writer,CancellationToken.None);
  Assert.Equal(1,result.TotalUnique);
  Assert.Equal(1,result.Employment);
  Assert.Equal(0,result.FreelanceProjects);
  Assert.Equal(1,result.EmploymentLikelyFit);
  var row=JsonDocument.Parse(writer.ToString());
  using(row)
  {
   Assert.Equal("Employment",row.RootElement.GetProperty("Kind").GetString());
   Assert.Equal("NOT_VERIFIED",row.RootElement.GetProperty("StatusEvidence").GetString());
   Assert.Equal(JsonValueKind.Null,row.RootElement.GetProperty("FirstObservedAt").ValueKind);
   Assert.Equal(JsonValueKind.Null,row.RootElement.GetProperty("RecordedDiscoveries").ValueKind);
   Assert.False(row.RootElement.TryGetProperty("Description",out _));
  }
 }

 [Fact]
 public async Task TypedInputsStaySeparateAndDoNotExposeProjectDescriptions()
 {
  var hunt=Hunt();
  var free=Freelancer();
  var files=new Dictionary<OpportunityInputKind,TextReader>
  {
   [OpportunityInputKind.Freelancehunt]=new StringReader(JsonlOutput.Serialize(hunt)),
   [OpportunityInputKind.Freelancer]=new StringReader(JsonlOutput.Serialize(free))
  };
  using var output=new StringWriter();
  var result=await OpportunityPreview.WriteAsync([Employment()],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],files,
   output,CancellationToken.None);
  Assert.Equal(3,result.TotalUnique);
  Assert.Equal(1,result.Employment);
  Assert.Equal(2,result.FreelanceProjects);
  Assert.Equal(2,result.FreelancePriority);
  Assert.Equal(2,result.Inputs.Count);
  Assert.All(result.Inputs,x=>Assert.Equal("SNAPSHOT_ONLY_UNVERIFIED_COVERAGE",x.CoverageStatus));
  Assert.DoesNotContain("Implement REST API for trading execution",output.ToString());
  Assert.DoesNotContain("ASP.NET Core broker exchange API integration",output.ToString());
  var rows=output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries)
   .Select(line=>JsonDocument.Parse(line)).ToArray();
  try
  {
   Assert.Equal(2,rows.Count(r=>r.RootElement.GetProperty("Kind").GetString()=="FreelanceProject"));
   Assert.Contains(rows,r=>r.RootElement.GetProperty("Source").GetString()=="freelancehunt");
   Assert.Contains(rows,r=>r.RootElement.GetProperty("Source").GetString()=="freelancer");
   Assert.Contains(rows,r=>r.RootElement.GetProperty("BudgetMinimum").ToString()=="3000");
  }
  finally{foreach(var r in rows)r.Dispose();}
 }

 [Fact]
 public async Task PlusRequirementSurvivesImportAndDowngradesProject()
 {
  var file=new Dictionary<OpportunityInputKind,TextReader>
  {[OpportunityInputKind.Freelancehunt]=new StringReader(JsonlOutput.Serialize(Hunt(plus:true)))};
  using var output=new StringWriter();
  var result=await OpportunityPreview.WriteAsync([],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],file,
   output,CancellationToken.None);
  Assert.Equal(1,result.FreelanceReview);
  Assert.Equal(0,result.FreelancePriority);
  Assert.Contains("Plus account requirement",output.ToString());
 }

 [Fact]
 public async Task DuplicatesAreByCanonicalUrlNotSimilarTitle()
 {
  var a=Wwr();
  var b=Wwr("wwr-programming");
  using var output=new StringWriter();
  var files=new Dictionary<OpportunityInputKind,TextReader>
  {[OpportunityInputKind.RemoteBoards]=new StringReader(
   JsonlOutput.Serialize(a)+"\n"+JsonlOutput.Serialize(b)+"\n")};
  var result=await OpportunityPreview.WriteAsync([],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],files,
   output,CancellationToken.None);
  Assert.Equal(1,result.TotalUnique);
  Assert.Equal(1,result.Inputs[0].Duplicates);
  Assert.Equal(2,result.Inputs[0].LinesRead);
  Assert.Single(output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries));
 }

 [Fact]
 public async Task SnapshotCannotAssertHighConfidenceFromTruncatedRemotiveText()
 {
  var opening=new RemoteBoardOpening("remotive",
   "https://remotive.com/remote-jobs/software-dev/dotnet-middle-42",
   "Middle .NET Backend","Company","Fully remote ASP.NET Core EF Core",
   DateTimeOffset.UtcNow,"Worldwide","contract",null,true,"Remotive");
  var resultRow=new RemoteBoardCandidate(opening,FitBucket.LikelyFit,97,
   ["Tech match"],["Unverified current status"]);
  using var output=new StringWriter();
  var result=await OpportunityPreview.WriteAsync([],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],
   new Dictionary<OpportunityInputKind,TextReader>
   {[OpportunityInputKind.RemoteBoards]=new StringReader(JsonlOutput.Serialize(resultRow))},
   output,CancellationToken.None);
  Assert.Equal(1,result.EmploymentNeedsReview);
  Assert.Equal(0,result.EmploymentLikelyFit);
  Assert.Contains("confidence downgraded",output.ToString());
 }

 [Theory]
 [InlineData("http://freelancehunt.com/project/bad/1.html")]
 [InlineData("https://freelancehunt.com.evil.example/project/bad/1.html")]
 [InlineData("https://freelancehunt.com/home")]
 public async Task UntrustedImportedProjectUrlsAreRejected(string url)
 {
  using var output=new StringWriter();
  var files=new Dictionary<OpportunityInputKind,TextReader>
  {[OpportunityInputKind.Freelancehunt]=new StringReader(JsonlOutput.Serialize(Hunt(url)))};
  var report=await OpportunityPreview.WriteAsync([],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],files,
   output,CancellationToken.None);
  Assert.Equal(0,report.TotalUnique);
  Assert.Equal(1,report.Inputs[0].Rejected);
  Assert.Equal("INPUT_ERRORS",report.Inputs[0].CoverageStatus);
  Assert.Empty(output.ToString());
 }

 [Fact]
 public async Task InvalidJsonIsCountedWithoutLoggingItsContent()
 {
  using var output=new StringWriter();
  var files=new Dictionary<OpportunityInputKind,TextReader>
  {[OpportunityInputKind.Freelancer]=new StringReader(
   "{invalid-secret-api-token}\n"+JsonlOutput.Serialize(Freelancer()))};
  var report=await OpportunityPreview.WriteAsync([],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],files,
   output,CancellationToken.None);
  Assert.Equal(2,report.Inputs[0].LinesRead);
  Assert.Equal(1,report.Inputs[0].Accepted);
  Assert.Equal(1,report.Inputs[0].Rejected);
  Assert.Equal(1,report.TotalUnique);
  Assert.DoesNotContain("invalid-secret-api-token",output.ToString());
 }

 [Fact]
 public async Task DiscoveryHistoryIsOnlyReportedWhenEvidenceExists()
 {
  var job=Employment();
  var original=new DateTimeOffset(2026,10,10,8,0,0,TimeSpan.Zero);
  var latest=original.AddHours(3);
  var history=new Dictionary<(string,string),JobDiscoveryHistory>
  {[(job.Source,job.Url)]=new(original,latest,4)};
  using var output=new StringWriter();
  await OpportunityPreview.WriteAsync([job],history,[],new Dictionary<OpportunityInputKind,TextReader>(),
   output,CancellationToken.None);
  using var doc=JsonDocument.Parse(output.ToString());
  Assert.Equal(4L,doc.RootElement.GetProperty("RecordedDiscoveries").GetInt64());
  Assert.Equal(original,doc.RootElement.GetProperty("FirstObservedAt").GetDateTimeOffset());
 }

 [Fact]
 public async Task LegacyNonHttpsVacancyIsNotSilentlyLost()
 {
  var job=Employment(url:"http://legacy.example/vacancy/1");
  using var output=new StringWriter();
  var report=await OpportunityPreview.WriteAsync([job],
   new Dictionary<(string,string),JobDiscoveryHistory>(),[],
   new Dictionary<OpportunityInputKind,TextReader>(),output,CancellationToken.None);
  Assert.Equal(1,report.TotalUnique);
  Assert.Contains("malformed",output.ToString());
 }

 [Fact]
 public async Task PostgreSqlDiscoveryEventsAreAtomicAndAdditive()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var id=Guid.NewGuid().ToString("N");
  var job=Employment("phase15-"+id,"https://jobs.dou.ua/vacancies/"+id);
  await storage.SaveDiscovered(new JobRef(job.Source,job.Url,job.Title,job.Company,
    job.PublishedAt),CancellationToken.None,"C#");
  await storage.SaveDiscovered(new JobRef(job.Source,job.Url,job.Title,job.Company,
    job.PublishedAt),CancellationToken.None,"backend");
  var histories=await storage.ReadDiscoveryHistoriesAsync(CancellationToken.None);
  var h=histories[(job.Source,job.Url)];
  Assert.Equal(2L,h.Observations);
  Assert.True(h.FirstObservedAt<=h.LastObservedAt);
  await using var db=new NpgsqlConnection(cs);
  await db.OpenAsync();
  await using var cmd=new NpgsqlCommand("""
SELECT count(*) FROM job_discoveries
WHERE source=@s AND url=@u AND query_name IN ('C#','backend')
""",db);
  cmd.Parameters.AddWithValue("s",job.Source);
  cmd.Parameters.AddWithValue("u",job.Url);
  Assert.Equal(2L,await cmd.ExecuteScalarAsync());
 }

 [Fact]
 public async Task PostgreSqlLatestCrawlCoverageRetainsProviderWarnings()
 {
  var cs=Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB");
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var source="phase15-coverage-"+Guid.NewGuid().ToString("N");
  var start=DateTimeOffset.UtcNow.AddMinutes(-1);
  await storage.SaveReport(new CrawlReport(start,DateTimeOffset.UtcNow,
   [new SourceResult(source,2,12,8,4,100,"PARTIAL","403",
    null,"reconciliation incomplete")]),CancellationToken.None);
  var reports=await storage.ReadLatestCoverageAsync(100,CancellationToken.None);
  var report=Assert.Single(reports,x=>x.Source==source);
  Assert.Equal(12,report.ReferencesFound);
  Assert.Equal(4,report.DetailsFailed);
  Assert.Equal(100,report.ReportedTotal);
  Assert.Equal("PARTIAL",report.Status);
  Assert.Equal("reconciliation incomplete",report.Warning);
 }
}
