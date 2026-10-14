using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class TargetReviewTests
{
 private static readonly DateTimeOffset Now=new(2026,10,14,11,0,0,TimeSpan.Zero);
 private static VacancySnapshot Job(int id,string title,string body,
  string source="dou",bool full=true,bool? open=null)=>
  new(source,$"https://jobs.dou.ua/vacancies/{id}/",title,null,
   full?null:body,full?body:"",full,open,Now.AddDays(-1),
   Now.AddHours(-1),["dou"]);
 private static readonly string Backend=
  "Fully remote ASP.NET Core REST API EF Core PostgreSQL RabbitMQ.";

 [Fact]
 public void BackendReviewOutranksHigherScoringReactAndSenior()
 {
  var good=Job(1,"Middle .NET Backend",Backend);
  var react=Job(2,"Middle .NET React FullStack",Backend+" React frontend");
  var senior=Job(3,"Senior .NET Backend",Backend);
  var report=TargetReview.Analyze([react,senior,good],
   new Dictionary<string,EmploymentDecision>(),3,Now);
  Assert.Equal(1,report.TargetBackendCandidates);
  Assert.Equal(1,report.Returned);
  Assert.Equal(good.Url,Assert.Single(report.Vacancies).Url);
  Assert.Equal(VacancyReviewTier.TargetDotNetBackend,
   report.Vacancies[0].Tier);
  Assert.Equal("OPEN_UNVERIFIED",report.Vacancies[0].OpeningEvidence);
 }

 [Fact]
 public void SuppressesAppliedAndInactiveWithoutRewritingProviderStatus()
 {
  var applied=Job(11,"Middle C# Backend",Backend);
  var inactive=Job(12,"Middle C# Backend",Backend);
  var keep=Job(13,"Middle C# Backend",Backend);
  var decisions=new Dictionary<string,EmploymentDecision>
  {
   [Storage.CanonicalEmploymentUrl(applied.Url)]=new(applied.Url,"Applied",Now),
   [Storage.CanonicalEmploymentUrl(inactive.Url)]=new(inactive.Url,"Inactive",Now)
  };
  var report=TargetReview.Analyze([applied,inactive,keep],decisions,3,Now);
  Assert.Equal(1,report.Returned);
  Assert.Equal(keep.Url,Assert.Single(report.Vacancies).Url);
 }

 [Fact]
 public void FullTextAndRemoteRankAheadOfIncompleteOrUnknownMode()
 {
  var remote=Job(21,"Junior C# Backend",Backend);
  var unknown=Job(22,"Junior C# Backend","ASP.NET Core REST API");
  var preview=Job(23,"Junior C# Backend",Backend,full:false);
  var result=TargetReview.Analyze([preview,unknown,remote],
   new Dictionary<string,EmploymentDecision>(),3,Now);
  Assert.Equal(remote.Url,result.Vacancies[0].Url);
  Assert.Equal(preview.Url,result.Vacancies[1].Url);
  Assert.Equal(unknown.Url,result.Vacancies[2].Url);
 }

 [Fact]
 public void ExplicitConstraintsStayOutsideTopQueue()
 {
  var office=Job(31,"Middle .NET Backend","Onsite only. "+Backend);
  var english=Job(32,"Middle .NET Backend",
   Backend+" English B2 required.");
  var eu=Job(33,"Middle .NET Backend",Backend+" Remote EU only.");
  var java=Job(34,"Middle Java Developer","Fully remote Spring Boot REST API");
  var result=TargetReview.Analyze([office,english,eu,java],
   new Dictionary<string,EmploymentDecision>(),4,Now);
  Assert.Empty(result.Vacancies);
 }

 [Fact]
 public void BoundTopThirtyAndExposeTruncatedSourceSnapshot()
 {
  var jobs=Enumerable.Range(1,35)
   .Select(x=>Job(1000+x,"Middle C# Backend",Backend)).ToArray();
  var report=TargetReview.Analyze(jobs,
   new Dictionary<string,EmploymentDecision>(),40,Now);
  Assert.Equal(35,report.TargetBackendCandidates);
  Assert.Equal(30,report.Returned);
  Assert.Equal(5,report.OmittedBySnapshotLimit);
  Assert.Equal(40,report.TotalStored);
  Assert.Throws<ArgumentOutOfRangeException>(()=>
   TargetReview.Analyze(jobs,new Dictionary<string,EmploymentDecision>(),34,Now));
 }

 [Fact]
 public void StoredPositiveIsNotCalledLiveVerified()
 {
  var job=Job(55,"Middle C# Backend",Backend,open:true) with {
   StatusEvidence="test:positive",StatusCheckedAt=Now.AddDays(-5)};
  var item=Assert.Single(TargetReview.Analyze([job],
   new Dictionary<string,EmploymentDecision>(),1,Now).Vacancies);
  Assert.Equal("HISTORICAL_POSITIVE_STATUS",item.OpeningEvidence);
  Assert.NotEqual(FitBucket.LikelyFit,
   VacancyRecommendationGates.Apply(VacancyTriage.Assess(job),Now).Bucket);
 }
}
