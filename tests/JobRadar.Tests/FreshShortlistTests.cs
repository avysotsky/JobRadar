using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class FreshShortlistTests
{
 private static readonly DateTimeOffset Now=
  new(2026,10,13,16,0,0,TimeSpan.Zero);
 private const string Target="Fully remote ASP.NET Core Web API REST API EF Core PostgreSQL RabbitMQ.";
 private static VacancySnapshot Job(string source,string id,string title,
  string description=Target,bool full=true,bool? open=null,
  DateTimeOffset? statusAt=null,string? evidence=null,string? url=null,
  DateTimeOffset? seen=null)=>
  new(source,url??("https://jobs.dou.ua/vacancies/"+id+"/"),title,null,
   full?null:description,full?description:"",full,open,
   Now.AddHours(-10),seen??Now.AddHours(-1),["test"],
   statusAt,evidence);

 private static FreshShortlistReport Shortlist(
  VacancySnapshot[] jobs,
  IReadOnlyDictionary<(string Source,string Url),DateTimeOffset>? first=null,
  IReadOnlyDictionary<string,EmploymentDecision>? decisions=null,
  long? total=null)
 {
  var known=first??jobs.ToDictionary(x=>(x.Source,x.Url),
   _=>Now.AddHours(-6));
  return FreshShortlist.Analyze(jobs,known,
   decisions??new Dictionary<string,EmploymentDecision>(),
   total??jobs.Length,Now);
 }

 [Fact]
 public void PrioritizesRecentlyObservedBackendAndLabelsOpenEvidenceSeparately()
 {
  var confident=Job("dou","1","Middle .NET Backend",open:true,
   statusAt:Now.AddHours(-1),evidence:"html:provider-open-evidence");
  var unverified=Job("dou","2","Junior C# Backend");
  var foreignBoard=Job("wwr","3","Middle .NET Backend",
   url:"https://weworkremotely.com/remote-jobs/example-backend",
   full:false);
  var report=Shortlist([unverified,foreignBoard,confident]);
  Assert.Equal(3,report.Returned);
  Assert.Equal(3,report.RecentRecordedJobs);
  Assert.Equal(confident.Url,report.Vacancies[0].Url);
  Assert.Equal(FitBucket.LikelyFit,report.Vacancies[0].Bucket);
  Assert.Equal("POSITIVE_EVIDENCE_RECORDED_24H",
   report.Vacancies[0].StatusEvidenceLevel);
  Assert.Equal(FitBucket.NeedsReview,report.Vacancies[1].Bucket);
  Assert.Equal("OPEN_UNVERIFIED",
   report.Vacancies[1].StatusEvidenceLevel);
  Assert.Equal("DISCOVERED_WITHIN_24H",
   report.Vacancies[0].DiscoveryRecency);
  Assert.All(report.Vacancies,x=>Assert.StartsWith("https://",x.Url));
  Assert.Contains("Ukraine",report.Interpretation);
 }

 [Fact]
 public void StalePositiveCannotBeAdvertisedAsFreshLikelyFit()
 {
  var staleStatus=Job("dou","4","Middle .NET Backend",open:true,
   statusAt:Now.AddDays(-5),evidence:"html:provider-open-evidence");
  var report=Shortlist([staleStatus]);
  var entry=Assert.Single(report.Vacancies);
  Assert.Equal(FitBucket.NeedsReview,entry.Bucket);
  Assert.Equal(VacancyReviewTier.TargetDotNetBackend,entry.ReviewTier);
  Assert.Equal("POSITIVE_EVIDENCE_STALE",entry.StatusEvidenceLevel);
  Assert.Contains(entry.Warnings,x=>x.StartsWith("SHORTLIST_STATUS_NOT_FRESH:"));
 }

 [Fact]
 public void ExcludesOwnerActionsSeniorHybridCountryRestrictionsAndOffTargetStacks()
 {
  var applied=Job("dou","11","Middle .NET Backend");
  var inactive=Job("dou","12","Middle .NET Backend");
  var senior=Job("dou","13","Senior .NET Backend");
  var hybrid=Job("dou","14","Middle .NET Backend","Hybrid 2 days office. "+Target);
  var euOnly=Job("dou","15","Middle .NET Backend","Remote EU only. "+Target);
  var java=Job("dou","16","Middle Java Backend","Fully remote Spring Boot");
  var mixed=Job("dou","17","Middle/Senior .NET Backend");
  var good=Job("dou","18","Junior C# Backend");
  var decisions=new Dictionary<string,EmploymentDecision>
  {
   [Storage.CanonicalEmploymentUrl(applied.Url)]=
    new(applied.Url,"Applied",Now),
   [Storage.CanonicalEmploymentUrl(inactive.Url)]=
    new(inactive.Url,"Inactive",Now)
  };
  var report=Shortlist([applied,inactive,senior,hybrid,euOnly,java,mixed,good],
   decisions:decisions);
  Assert.Equal(8,report.RecentRecordedJobs);
  Assert.Equal(1,report.Returned);
  Assert.Equal(good.Url,Assert.Single(report.Vacancies).Url);
 }

 [Fact]
 public void FirstRecordedBoundariesAndMissingHistoryAreNotInvented()
 {
  var included=Job("dou","21","Middle .NET Backend");
  var tooOld=Job("dou","22","Middle .NET Backend");
  var missing=Job("dou","23","Middle .NET Backend");
  var future=Job("dou","24","Middle .NET Backend");
  var first=new Dictionary<(string Source,string Url),DateTimeOffset>
  {
   [(included.Source,included.Url)]=Now.AddDays(-7),
   [(tooOld.Source,tooOld.Url)]=Now.AddDays(-8),
   [(future.Source,future.Url)]=Now.AddMinutes(1)
  };
  var report=Shortlist([included,tooOld,missing,future],first);
  Assert.Equal(1,report.RecentRecordedJobs);
  Assert.Equal(included.Url,Assert.Single(report.Vacancies).Url);
  Assert.Equal(Now.AddDays(-7),report.FirstRecordedSince);
 }

 [Theory]
 [InlineData("dou","https://evil.example.org/vacancies/1/")]
 [InlineData("dou","http://jobs.dou.ua/vacancies/1/")]
 [InlineData("dou","https://jobs.dou.ua/vacancies/1/#test")]
 [InlineData("djinni","https://djinni.co:444/jobs/1-example/")]
 [InlineData("wwr","https://weworkremotely.com.evil.org/remote-jobs/a")]
 [InlineData("robota","https://robota.ua@evil.com/company1/vacancy2")]
 [InlineData("unknown","https://jobs.dou.ua/vacancies/1/")]
 [InlineData("dou","https://jobs.dou.ua/redirect?to=https://evil.example/")]
 [InlineData("dou","https://jobs.dou.ua/vacancies/123/?ref=tracker")]
 [InlineData("djinni","https://djinni.co/jobs/")]
 [InlineData("robota","https://robota.ua/company1/")]
 [InlineData("wwr","https://weworkremotely.com/account/login")]
 public void RejectsLinksOutsideKnownOriginalSource(string source,string url)
 {
  Assert.False(FreshShortlist.IsOriginalTrustedLink(source,url));
 }

 [Theory]
 [InlineData("dou","https://jobs.dou.ua/vacancies/1/")]
 [InlineData("djinni","https://djinni.co/jobs/1-example/")]
 [InlineData("robota","https://robota.ua/company1/vacancy2")]
 [InlineData("wwr","https://weworkremotely.com/remote-jobs/a")]
 public void KeepsDirectOriginalSourceLinks(string source,string url)
 {
  Assert.True(FreshShortlist.IsOriginalTrustedLink(source,url));
 }

 [Fact]
 public void BoundedTopTwentyAndIncompleteDatabaseAreExplicit()
 {
  var jobs=Enumerable.Range(1,25)
   .Select(i=>Job("dou","999"+i,"Middle .NET Backend"))
   .ToArray();
  var report=Shortlist(jobs,total:30);
  Assert.Equal(25,report.EligibleForReview);
  Assert.Equal(20,report.Returned);
  Assert.Equal(20,report.Vacancies.Count);
  Assert.Equal(5,report.OmittedBySnapshotLimit);
  Assert.Equal(30,report.TotalStored);
  Assert.All(report.Vacancies,x=>Assert.Equal(FitBucket.NeedsReview,x.Bucket));
 }

 [Fact]
 public void OlderLastSeenIsFlaggedButNotConfusedWithEmployerClosure()
 {
  var oldSeen=Job("dou","41","Middle .NET Backend",seen:Now.AddDays(-3));
  var item=Assert.Single(Shortlist([oldSeen]).Vacancies);
  Assert.Equal("LAST_SEEN_OVER_24H",item.DiscoveryRecency);
  Assert.Equal("OPEN_UNVERIFIED",item.StatusEvidenceLevel);
  Assert.Equal(FitBucket.NeedsReview,item.Bucket);
 }

 [Fact]
 public void FirstRecordedDoesNotConflateOldEmployerPostingWithNewFeedObservation()
 {
  var oldPosting=Job("dou","88","Middle .NET Backend") with
  {
   PublishedAt=Now.AddDays(-30)
  };
  var futurePosting=Job("dou","89","Middle .NET Backend") with
  {
   PublishedAt=Now.AddDays(1)
  };
  var result=Shortlist([oldPosting,futurePosting]);
  var old=Assert.Single(result.Vacancies,x=>x.Url==oldPosting.Url);
  var future=Assert.Single(result.Vacancies,x=>x.Url==futurePosting.Url);
  Assert.Contains(old.Warnings,x=>
   x.StartsWith("POSTED_BEFORE_DISCOVERY_WINDOW:",StringComparison.Ordinal));
  Assert.Contains(future.Warnings,x=>
   x.StartsWith("FUTURE_PUBLICATION_DATE:",StringComparison.Ordinal));
  Assert.Equal(2,result.RecentRecordedJobs);
 }

 [Fact]
 public void InvalidTotalFailsBeforeCalculatingCandidateCounts()
 {
  var job=Job("dou","70","Middle .NET Backend");
  Assert.Throws<ArgumentOutOfRangeException>(()=>
   Shortlist([job],total:0));
 }
}
