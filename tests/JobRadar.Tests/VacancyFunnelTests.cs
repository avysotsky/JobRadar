using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class VacancyFunnelTests
{
 private static readonly DateTimeOffset Now=
  new(2026,10,13,12,0,0,TimeSpan.Zero);
 private const string TargetBody=
  "Fully remote ASP.NET Core Web API REST API EF Core PostgreSQL RabbitMQ.";

 private static VacancySnapshot Job(string source,string id,string title,
  string body,bool full=true,bool? opened=null,string? evidence=null,
  DateTimeOffset? lastSeen=null)=>
  new(source,"https://jobs.dou.ua/vacancies/"+id+"/",title,null,
   full?null:body,full?body:"",full,opened,Now.AddDays(-2),
   lastSeen??Now,["C#"],evidence is null?null:Now,evidence);

 [Fact]
 public void ReportsActionableEvidenceWithoutEquatingUnknownWithClosed()
 {
  var jobs=new[]
  {
   Job("dou","1","Middle .NET Backend",TargetBody,true,true,"test:positive"),
   Job("dou","2","Middle .NET Backend",TargetBody),
   Job("dou","3","Middle .NET Backend",TargetBody,true,false,
    "html:explicit-closed-banner"),
   Job("dou","4","Middle .NET Backend",TargetBody),
   Job("dou","5","Middle .NET Backend","Remote EU only. "+TargetBody),
   Job("dou","6","Middle .NET Backend",TargetBody+" English B2 required."),
   Job("dou","7","Middle .NET Backend",TargetBody,lastSeen:Now.AddHours(-30)),
   Job("wwr","8","Unspecified Developer","Remote position",full:false)
  };
  var appliedUrl=jobs[3].Url;
  var decisions=new Dictionary<string,EmploymentDecision>
  {
   [Storage.CanonicalEmploymentUrl(appliedUrl)]=
    new(appliedUrl,"Applied",Now)
  };
  var report=VacancyFunnel.Analyze(jobs,decisions,11,Now);
  Assert.Equal(8,report.Evaluated);
  Assert.Equal(3,report.OmittedByLimit);
  Assert.Equal(2,report.Sources.Count);
  Assert.Equal(8,report.Totals.Stored);
  Assert.Equal(7,report.Totals.FullText);
  Assert.Equal(1,report.Totals.PreviewOnly);
  Assert.Equal(7,report.Totals.SeenWithin24Hours);
  Assert.Equal(1,report.Totals.PositiveStatusEvidenceRecorded);
  Assert.Equal(1,report.Totals.ExplicitProviderClosed);
  Assert.Equal(6,report.Totals.StatusUnverified);
  Assert.Equal(1,report.Totals.GeographicRestriction);
  Assert.Equal(1,report.Totals.EnglishB2PlusMentioned);
  Assert.Equal(1,report.Totals.OwnerApplied);
  Assert.Equal(1,report.Totals.LikelyFit);
  Assert.Equal(5,report.Totals.NeedsReview);
  Assert.Equal(2,report.Totals.Excluded);
  Assert.Equal(2,report.Totals.ReviewTargetBackend);
  Assert.Equal(2,report.Totals.ReviewDifficultRequirements);
  Assert.Equal(1,report.Totals.ReviewOtherSpecialization);
  Assert.Equal(report.Totals.NeedsReview,
   report.Totals.ReviewTargetBackend+
   report.Totals.ReviewPartialDotNet+
   report.Totals.ReviewDifficultRequirements+
   report.Totals.ReviewOtherSpecialization);
  Assert.Equal(7,report.Sources[0].Stored);
  Assert.Equal("dou",report.Sources[0].Source);
  Assert.Equal("wwr",report.Sources[1].Source);
 }

 [Fact]
 public void NoRowsAndTruncationStayExplicitInsteadOfFabricatingZeroCoverage()
 {
  var result=VacancyFunnel.Analyze([],new Dictionary<string,EmploymentDecision>(),12,Now);
  Assert.Equal(0,result.Evaluated);
  Assert.Equal(12,result.OmittedByLimit);
  Assert.Empty(result.Sources);
  Assert.Equal(0,result.Totals.LikelyFit);
  Assert.Throws<ArgumentOutOfRangeException>(()=>
   VacancyFunnel.Analyze([],new Dictionary<string,EmploymentDecision>(),-1,Now));
 }

 [Fact]
 public void UserInactiveIsNotProviderClosedAndStalePositiveIsNotLikelyFit()
 {
  var inactive=Job("dou","18","Middle .NET Backend",TargetBody);
  var stale=Job("dou","19","Middle .NET Backend",TargetBody,
   opened:true,evidence:"test:positive",lastSeen:Now.AddHours(-30));
  var decisions=new Dictionary<string,EmploymentDecision>
  {
   [Storage.CanonicalEmploymentUrl(inactive.Url)]=
    new(inactive.Url,"Inactive",Now)
  };
  var row=VacancyFunnel.Analyze([inactive,stale],decisions,2,Now).Totals;
  Assert.Equal(1,row.OwnerInactive);
  Assert.Equal(0,row.ExplicitProviderClosed);
  Assert.Equal(1,row.PositiveStatusEvidenceRecorded);
  Assert.Equal(1,row.StatusUnverified);
  Assert.Equal(0,row.LikelyFit);
  Assert.Equal(1,row.Excluded);
  Assert.Equal(1,row.NeedsReview);
 }

 [Fact]
 public async Task PostgreSqlStoredVacanciesAndOwnDecisionsFeedReadOnlyAudit()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var store=new Storage(cs);
  await store.Initialize(CancellationToken.None);
  var suffix=Guid.NewGuid().ToString("N");
  var source="phase33-"+suffix;
  var first=new JobRef(source,"https://jobs.dou.ua/vacancies/"+suffix+"-a/",
   "Middle .NET Backend",null,Now);
  var second=new JobRef(source,"https://jobs.dou.ua/vacancies/"+suffix+"-b/",
   "Middle .NET Backend",null,Now);
  foreach(var job in new[]{first,second})
  {
   await store.SaveDiscovered(job,CancellationToken.None,"C#");
   await store.Save(new JobDetail(job,TargetBody+new string('X',100),
    null,DateTimeOffset.UtcNow,"html:status-unverified"),CancellationToken.None);
  }
  await store.MarkEmploymentDecisionAsync(second.Url,"Applied",CancellationToken.None);
  var snapshots=(await store.ReadVacanciesForTriageAsync(50000,CancellationToken.None))
   .Where(x=>x.Source==source).ToArray();
  var decisions=await store.ReadEmploymentDecisionsAsync(CancellationToken.None);
  var report=VacancyFunnel.Analyze(snapshots,decisions,2,DateTimeOffset.UtcNow);
  var row=Assert.Single(report.Sources);
  Assert.Equal(source,row.Source);
  Assert.Equal(2,row.Stored);
  Assert.Equal(2,row.FullText);
  Assert.Equal(1,row.OwnerApplied);
  Assert.Equal(1,row.Excluded);
  Assert.Equal(1,row.NeedsReview);
  Assert.Equal(2,row.StatusUnverified);
 }
}
