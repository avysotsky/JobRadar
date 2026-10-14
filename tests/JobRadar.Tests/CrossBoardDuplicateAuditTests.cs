using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class CrossBoardDuplicateAuditTests
{
 private static readonly DateTimeOffset Now=
  new(2026,10,14,11,0,0,TimeSpan.Zero);

 // Long enough to exercise unique shingle comparison, not a title-only
 // heuristic. Whitespace and punctuation vary between DOU and Djinni HTML.
 private static readonly string Body=string.Join(" ",Enumerable.Range(1,24)
  .Select(i=>$"Ми розробляємо державні цифрові сервіси. "+
   $"Система номер {i} використовує ASP.NET Core, RabbitMQ, EF Core, "+
   $"PostgreSQL, мікросервіси та асинхронні REST API."));
 private static readonly string Unrelated=string.Join(" ",Enumerable.Range(1,24)
  .Select(i=>$"Ми розробляємо державні цифрові сервіси. "+
   $"Тест номер {i} використовує бухгалтерський облік, закупівлі, "+
   $"продажі, контент менеджмент та роботу з рекламними кампаніями."));
 private static VacancySnapshot Job(string source,string url,string title,
  string body,bool full=true,string? company=null)=>
  new(source,url,title,company,null,body,full,null,
   Now.AddDays(-1),Now,["test"]);
 private static VacancySnapshot Dou(string? body=null,bool full=true)=>
  Job("dou","https://jobs.dou.ua/companies/strimco/vacancies/362043/",
   "Middle .NET Developer в Strimco, Київ, віддалено",body??Body,full,
   "Strimco");
 private static VacancySnapshot Djinni(string? body=null,bool full=true)=>
  Job("djinni","https://djinni.co/jobs/831444-middle-net-developer/",
   "Middle .NET Developer",body??Body,full,null);
 private static CrossBoardDuplicateReport Audit(
  VacancySnapshot[] jobs,IReadOnlyDictionary<string,EmploymentDecision>? decisions=null,
  long? total=null)=>
  CrossBoardDuplicateAudit.Analyze(jobs,
   decisions??new Dictionary<string,EmploymentDecision>(),total??jobs.Length,Now);

 [Fact]
 public void StrimcoStyleCrossBoardDuplicatesMatchDespiteTitleSuffixAndFormatting()
 {
  var w=Dou();
  var djinni=Djinni(Body.Replace(". ",".\n").Replace(" та ","  та  "));
  var report=Audit([w,djinni]);
  var pair=Assert.Single(report.Pairs);
  Assert.Equal(2,report.FullTextCandidates);
  Assert.Equal(1,report.MatchedPairs);
  Assert.Equal(1,report.ReturnedPairs);
  Assert.True(pair.TextSimilarity>=0.96);
  Assert.Contains("POSSIBLE_DUPLICATE",pair.Evidence);
  Assert.Equal("dou",pair.First.Source);
  Assert.Equal("djinni",pair.Second.Source);
  Assert.Equal("Strimco",pair.First.Company);
  Assert.Null(pair.Second.Company);
  Assert.Equal("Middle .NET Developer",pair.Second.Title);
  Assert.Equal(0,report.OmittedBySnapshotLimit);
 }

 [Fact]
 public void SameTitleButDifferentFullDescriptionIsNeverEnough()
 {
  var report=Audit([Dou(),Djinni(Unrelated)]);
  Assert.Empty(report.Pairs);
  Assert.Equal(0,report.MatchedPairs);
  Assert.Equal(2,report.FullTextCandidates);
 }

 [Fact]
 public void OwnerAppliedAndInactiveAreDisplayedButNeverInherited()
 {
  var dou=Dou();var djinni=Djinni();
  var decisions=new Dictionary<string,EmploymentDecision>
  {
   [Storage.CanonicalEmploymentUrl(dou.Url)]=
    new(dou.Url,"Applied",Now)
  };
  var pair=Assert.Single(Audit([dou,djinni],decisions).Pairs);
  Assert.Equal("Applied",pair.First.OwnerDecision);
  Assert.Null(pair.Second.OwnerDecision);
  Assert.NotEqual(pair.First.Url,pair.Second.Url);
 }

 [Fact]
 public void SameSourceOrShortPreviewCannotCreateCrossBoardPair()
 {
  var a=Dou();
  var sameBoard=Job("dou","https://jobs.dou.ua/vacancies/12345/",
   "Middle .NET Developer",Body);
  var preview=Djinni(Body,false);
  Assert.Empty(Audit([a,sameBoard,preview]).Pairs);
  Assert.Equal(2,Audit([a,sameBoard,preview]).FullTextCandidates);
  var shortBody=Djinni("Fully remote .NET Backend EF Core and PostgreSQL.",true);
  Assert.Empty(Audit([a,shortBody]).Pairs);
  Assert.Equal(1,Audit([a,shortBody]).FullTextCandidates);
 }

 [Fact]
 public void DeniesUntrustedOrUnsupportedLinksAndUnknownSource()
 {
  var malicious=Job("djinni","https://djinni.co/jobs/1?next=https://evil.example",
   "Middle .NET Developer",Body);
  var wwr=Job("wwr","https://weworkremotely.com/remote-jobs/example",
   "Middle .NET Developer",Body);
  var robota=Job("robota","https://robota.ua/company1/vacancy2",
   "Middle .NET Developer",Body);
  var output=Audit([Dou(),malicious,wwr,robota]);
  Assert.Empty(output.Pairs);
  Assert.Equal(1,output.FullTextCandidates);
  Assert.Contains("Robota preview-only",output.Interpretation);
 }

 [Fact]
 public void DifferentJobTitlesDoNotMergeEvenIfEmployerCopiesFullDescription()
 {
  var w=Dou();
  var different=Job("djinni","https://djinni.co/jobs/1234-senior-net-backend/",
   "Senior .NET Backend",Body);
  Assert.Empty(Audit([w,different]).Pairs);
 }

 [Fact]
 public void ReportExposesTruncatedInputAndKeepsSortedDeterministicLinks()
 {
  var dou=Dou();var djinni=Djinni();
  var a=Audit([djinni,dou],total:20);
  var b=Audit([dou,djinni],total:20);
  Assert.Equal(18,a.OmittedBySnapshotLimit);
  Assert.Equal(18,b.OmittedBySnapshotLimit);
  Assert.Equal(a.Pairs.Select(x=>x.First.Url),
   b.Pairs.Select(x=>x.First.Url));
  Assert.Equal(1,a.MatchedPairs);
  Assert.Equal(1,b.MatchedPairs);
 }

 [Fact]
 public void FailClosedOnInvalidInputOrRepeatedVacancy()
 {
  var a=Dou();
  Assert.Throws<ArgumentOutOfRangeException>(()=>Audit([a],total:0));
  Assert.Throws<InvalidDataException>(()=>Audit([a,a]));
  Assert.Throws<ArgumentOutOfRangeException>(()=>
   Audit(Enumerable.Repeat(a,CrossBoardDuplicateAudit.MaxInput+1).ToArray()));
 }

 [Fact]
 public void NullEvidenceAndEmptyInputsRemainAuditable()
 {
  var empty=Audit([]);
  Assert.Equal(0,empty.Evaluated);
  Assert.Equal(0,empty.MatchedPairs);
  Assert.Empty(empty.Pairs);
  Assert.Equal(0,empty.OmittedPairs);
 }
}
