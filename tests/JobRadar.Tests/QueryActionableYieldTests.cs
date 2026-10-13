using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class QueryActionableYieldTests
{
 private static readonly DateTimeOffset Now=new(2026,10,13,16,0,0,TimeSpan.Zero);
 private const string Backend="Fully remote ASP.NET Core Web API EF Core PostgreSQL RabbitMQ.";
 private static SourceResult Row(string query,int count)=>
  new(query,1,count,0,0,null,"PARTIAL",null,RawRecords:count,DroppedRecords:0);
 private static CrawlReport Run(params SourceResult[] rows)=>
  new(Now.AddMinutes(-1),Now,rows);
 private static DiscoveryGapObservation Event(string provider,string query,string url)=>
  new(provider,query,url);
 private static VacancySnapshot Job(string provider,string id,string title,
  string body=Backend,bool full=true,bool? open=null,string? evidence=null)=>
  new(provider,"https://jobs.dou.ua/vacancies/"+id+"/",title,null,
   full?null:body,full?body:"",full,open,Now.AddDays(-1),Now,
   ["saved-query"],evidence is null?null:Now,evidence);
 private static QueryActionableYieldReport Analyze(
  CrawlReport run,DiscoveryGapObservation[] events,
  VacancySnapshot[] jobs,IReadOnlyDictionary<string,EmploymentDecision>? decisions=null,
  long? total=null)
 {
  var gaps=DiscoveryGapAudit.Analyze(run,events);
  return QueryActionableYield.Analyze(run,gaps,events,jobs,
   decisions??new Dictionary<string,EmploymentDecision>(),
   total??jobs.Length,Now);
 }

 [Fact]
 public void MarginalQueriesMeasureCurrentBackendReviewsWithoutInflatingOverlaps()
 {
  var jobs=new[]{
   Job("dou","1","Middle .NET Backend",open:true,evidence:"test:open"),
   Job("dou","2","Middle .NET Backend"),
   Job("dou","3","Middle .NET Backend"),
   Job("djinni","4","Senior .NET Backend"),
   Job("wwr","5","Middle C# Backend",full:false)
  };
  var report=Run(Row("dou",2),Row("dou-rss-C#",2),
   Row("djinni",1),Row("wwr-backend",1));
  var events=new[]{
   Event("dou","dou",jobs[0].Url),
   Event("dou","dou",jobs[1].Url),
   Event("dou","dou-rss-C#",jobs[1].Url),
   Event("dou","dou-rss-C#",jobs[2].Url),
   Event("djinni","djinni",jobs[3].Url),
   Event("wwr","wwr-backend",jobs[4].Url)
  };
  var result=Analyze(report,events,jobs);
  Assert.Equal(4,result.Queries.Count);
  var dou=Assert.Single(result.Queries,x=>x.Query=="dou");
  Assert.Equal(2,dou.MarginalUrls);
  Assert.Equal(1,dou.CurrentLikelyFit);
  Assert.Equal(1,dou.CurrentTargetBackendReview);
  Assert.Equal(1,dou.CurrentStatusUnknown);
  var csharp=Assert.Single(result.Queries,x=>x.Query=="dou-rss-C#");
  Assert.Equal(1,csharp.MarginalUrls);
  Assert.Equal(1,csharp.CurrentTargetBackendReview);
  Assert.Equal(1,csharp.CurrentStatusUnknown);
  Assert.Equal(2,result.CurrentTargetBackendMarginalByProvider["dou"]);
  Assert.Equal(0,result.CurrentTargetBackendMarginalByProvider["djinni"]);
  Assert.Equal(1,result.CurrentTargetBackendMarginalByProvider["wwr"]);
  Assert.Equal(1,Assert.Single(result.Queries,x=>x.Provider=="djinni").CurrentExcluded);
  Assert.All(result.Queries,x=>
  {
   Assert.Contains("CURRENT_FIT_NOT_AT_DISCOVERY_TIME",x.Signals);
   Assert.DoesNotContain("CURRENT_VACANCY_SNAPSHOT_MISSING",x.Signals);
  });
 }

 [Fact]
 public void OwnerAppliedSuppressesRecommendationsButKeepsHistoricalQueryProvenance()
 {
  var job=Job("dou","8","Middle .NET Backend");
  var decisions=new Dictionary<string,EmploymentDecision>
  {
   [Storage.CanonicalEmploymentUrl(job.Url)]=
    new(job.Url,"Applied",Now)
  };
  var run=Run(Row("dou",1));
  var result=Analyze(run,[Event("dou","dou",job.Url)],[job],decisions);
  var row=Assert.Single(result.Queries);
  Assert.Equal(1,row.MarginalUrls);
  Assert.Equal(0,row.CurrentTargetBackendReview);
  Assert.Equal(1,row.CurrentExcluded);
  Assert.Equal(1,row.CurrentStatusUnknown);
 }

 [Fact]
 public void MissingCurrentSnapshotFailsClosedEvenIfEventCountsMatch()
 {
  var a=Job("dou","11","Middle .NET Backend");
  var b=Job("dou","12","Middle .NET Backend");
  var run=Run(Row("dou",2));
  var result=Analyze(run,[
   Event("dou","dou",a.Url),Event("dou","dou",b.Url)
  ],[a],total:2);
  var row=Assert.Single(result.Queries);
  Assert.True(row.ObservedUrls==2 && row.MatchedCurrentSnapshots==1);
  Assert.Null(row.MarginalUrls);
  Assert.Null(row.CurrentTargetBackendReview);
  Assert.Contains("CURRENT_VACANCY_SNAPSHOT_MISSING",row.Signals);
  Assert.Contains("CURRENT_SNAPSHOT_TRUNCATED",row.Signals);
  Assert.Null(result.CurrentTargetBackendMarginalByProvider["dou"]);
  Assert.Equal(1,result.CurrentSnapshotOmitted);
 }

 [Fact]
 public void InvalidEarlierQueryTaintsOnlySameProviderMarginalFit()
 {
  var a=Job("dou","21","Middle .NET Backend");
  var b=Job("dou","22","Middle .NET Backend");
  var c=Job("djinni","23","Middle .NET Backend");
  var run=Run(Row("dou",2),Row("dou-rss-C#",1),Row("djinni",1));
  var result=Analyze(run,[
   Event("dou","dou",a.Url),
   Event("dou","dou-rss-C#",b.Url),
   Event("djinni","djinni",c.Url)
  ],[a,b,c]);
  Assert.Null(Assert.Single(result.Queries,x=>x.Query=="dou").MarginalUrls);
  var supplement=Assert.Single(result.Queries,x=>x.Query=="dou-rss-C#");
  Assert.Null(supplement.MarginalUrls);
  Assert.Contains("RUN_QUERY_ATTRIBUTION_UNVERIFIED",supplement.Signals);
  Assert.Null(result.CurrentTargetBackendMarginalByProvider["dou"]);
  Assert.Equal(1,result.CurrentTargetBackendMarginalByProvider["djinni"]);
 }

 [Fact]
 public void ValidZeroOverlapIsNotInventedNewTargetVacancies()
 {
  var job=Job("dou","31","Middle .NET Backend");
  var run=Run(Row("dou",1),Row("dou-rss-C#",1));
  var report=Analyze(run,[
   Event("dou","dou",job.Url),Event("dou","dou-rss-C#",job.Url)
  ],[job]);
  var overlap=Assert.Single(report.Queries,x=>x.Query=="dou-rss-C#");
  Assert.Equal(0,overlap.MarginalUrls);
  Assert.Equal(0,overlap.CurrentTargetBackendReview);
  Assert.Equal(1,overlap.MatchedCurrentSnapshots);
 }

 [Fact]
 public void MismatchedCrawlOrTooSmallTotalFailsClosed()
 {
  var run=Run(Row("dou",1));
  var events=new[]{Event("dou","dou","https://jobs.dou.ua/vacancies/90/")};
  var job=Job("dou","90","Middle .NET Backend");
  var otherRun=new CrawlReport(run.StartedAt.AddDays(-1),
   run.EndedAt.AddDays(-1),run.Sources);
  Assert.Throws<InvalidDataException>(()=>
   QueryActionableYield.Analyze(otherRun,
    DiscoveryGapAudit.Analyze(run,events),events,[job],
    new Dictionary<string,EmploymentDecision>(),1,Now));
  Assert.Throws<ArgumentOutOfRangeException>(()=>
   QueryActionableYield.Analyze(run,DiscoveryGapAudit.Analyze(run,events),
    events,[job],new Dictionary<string,EmploymentDecision>(),0,Now));
 }

 [Fact]
 public void NoStoredQueriesDoesNotBecomeSiteWideZeroVacancies()
 {
  var run=Run();
  var result=Analyze(run,[],[],total:15);
  Assert.Empty(result.Queries);
  Assert.Empty(result.CurrentTargetBackendMarginalByProvider);
  Assert.Equal(15,result.CurrentSnapshotOmitted);
 }
}
