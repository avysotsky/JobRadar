using System.Text;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class TargetReviewFileTests
{
 private static readonly DateTimeOffset Now=new(2026,10,14,11,0,0,TimeSpan.Zero);
 private static VacancySnapshot Job(string title="Middle .NET Backend",
  string body="Fully remote ASP.NET Core REST API EF Core PostgreSQL.")=>
  new("djinni","https://djinni.co/jobs/123-middle-net/",title,null,null,
   body,true,null,Now.AddDays(-1),Now,["djinni"],Now,"html:status-unverified");

 private static string TempPath()=>Path.Combine(Path.GetTempPath(),
  "jobradar-phase40-"+Guid.NewGuid().ToString("N")+".jsonl");

 [Fact]
 public async Task RecomputesPriorityFromVacancyAndDoesNotTrustExportedScore()
 {
  var path=TempPath();
  try
  {
   // This legacy projection lies about fit and score on purpose. Only its
   // original Vacancy may supply role and status evidence.
   await File.WriteAllTextAsync(path,JsonlOutput.Serialize(new
   {
    Vacancy=Job(),Bucket="Excluded",Score=-999,WorkMode="Onsite"
   })+"\n",new UTF8Encoding(false,true));
   var result=await TargetReviewFile.AnalyzeFileAsync(path,CancellationToken.None);
   Assert.Equal("TRIAGE_ALL_JSONL",result.InputKind);
   Assert.Equal(1,result.SourceRows);
   Assert.False(result.ManualDecisionsAvailable);
   Assert.Equal(1,result.Review.TargetBackendCandidates);
   var item=Assert.Single(result.Review.Vacancies);
   Assert.Equal("Middle .NET Backend",item.Title);
   Assert.Equal(WorkMode.Remote,item.WorkMode);
   Assert.True(item.Score>0);
   Assert.Equal("OPEN_UNVERIFIED",item.OpeningEvidence);
   Assert.Contains("Applied/Inactive",result.Interpretation);
  }
  finally { File.Delete(path); }
 }

 [Fact]
 public async Task PreservesUkrainianUtf8Text()
 {
  var path=TempPath();
  try
  {
   var title="Middle .NET бекенд розробник";
   var body="Віддалена робота з України. ASP.NET Core REST API PostgreSQL.";
   await File.WriteAllTextAsync(path,
    JsonlOutput.Serialize(new {Vacancy=Job(title,body)})+"\n",
    new UTF8Encoding(false,true));
   var result=await TargetReviewFile.AnalyzeFileAsync(path,CancellationToken.None);
   Assert.Equal(title,Assert.Single(result.Review.Vacancies).Title);
  }
  finally { File.Delete(path); }
 }

 [Fact]
 public async Task RejectsMissingVacancyRatherThanFallingBackToRawScore()
 {
  var path=TempPath();
  try
  {
   await File.WriteAllTextAsync(path,"{\"Score\":99}\n");
   await Assert.ThrowsAsync<InvalidDataException>(()=>
    TargetReviewFile.AnalyzeFileAsync(path,CancellationToken.None));
  }
  finally { File.Delete(path); }
 }

 [Fact]
 public async Task RejectsInvalidUtf8()
 {
  var path=TempPath();
  try
  {
   await File.WriteAllBytesAsync(path,[0xff,0xfe,0x0a]);
   await Assert.ThrowsAnyAsync<DecoderFallbackException>(()=>
    TargetReviewFile.AnalyzeFileAsync(path,CancellationToken.None));
  }
  finally { File.Delete(path); }
 }

 [Fact]
 public async Task RejectsDuplicateSourceUrlToPreventMisleadingCounts()
 {
  var path=TempPath();
  try
  {
   var line=JsonlOutput.Serialize(new {Vacancy=Job()});
   await File.WriteAllTextAsync(path,line+"\n"+line+"\n");
   var ex=await Assert.ThrowsAsync<InvalidDataException>(()=>
    TargetReviewFile.AnalyzeFileAsync(path,CancellationToken.None));
   Assert.Contains("Duplicate",ex.Message);
  }
  finally { File.Delete(path); }
 }

 [Fact]
 public async Task RejectsEmptyAndMalformedFiles()
 {
  var path=TempPath();
  try
  {
   await File.WriteAllTextAsync(path,"");
   await Assert.ThrowsAsync<InvalidDataException>(()=>
    TargetReviewFile.AnalyzeFileAsync(path,CancellationToken.None));
   await File.WriteAllTextAsync(path,"{not-json}\n");
   await Assert.ThrowsAsync<InvalidDataException>(()=>
    TargetReviewFile.AnalyzeFileAsync(path,CancellationToken.None));
  }
  finally { File.Delete(path); }
 }

 [Fact]
 public async Task ExcludesKnownSeniorAndReactInsteadOfSortingByScore()
 {
  var path=TempPath();
  try
  {
   var good=Job();
   var senior=Job("Senior .NET Backend") with {
    Url="https://djinni.co/jobs/124-senior-net/" };
   var react=Job("Middle .NET React FullStack") with {
    Url="https://djinni.co/jobs/125-react-net/" };
   var lines=new[]{good,senior,react}
    .Select(job=>JsonlOutput.Serialize(new {Vacancy=job}));
   await File.WriteAllLinesAsync(path,lines,new UTF8Encoding(false,true));
   var result=await TargetReviewFile.AnalyzeFileAsync(path,CancellationToken.None);
   Assert.Equal(3,result.SourceRows);
   Assert.Equal(good.Url,Assert.Single(result.Review.Vacancies).Url);
  }
  finally { File.Delete(path); }
 }
}
