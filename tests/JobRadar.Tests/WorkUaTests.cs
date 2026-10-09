using JobRadar;
using Xunit;
namespace JobRadar.Tests;
public sealed class WorkUaTests
{
 [Fact] public void ExtractsCanonicalVacancyLinksAndRejectsExternal()
 {
  var html="""
  <main><a href="/jobs/6775849/"><h2>Strong Middle .NET Developer</h2></a>
  <a href="/jobs/6775849/">Duplicate</a>
  <a href="https://evil.example/jobs/890/">External</a>
  <a href="/jobs-remote-c%23/">Search</a></main>
  """;
  var job=Assert.Single(new WorkUaSource("c#").ParseListings(html));
  Assert.Equal("workua",job.Source);
  Assert.Equal("https://www.work.ua/jobs/6775849/",job.Url);
  Assert.Equal("Strong Middle .NET Developer",job.Title);
 }
 [Fact] public void DetailDescriptionRequiresMeaningfulContent()
 {
  var html="<div itemprop='description'><p>"+
     new string('A',130)+"</p></div>";
  Assert.Equal(new string('A',130),Parsers.Description("workua-c#",html));
  Assert.Equal("",Parsers.Description("workua-c#","<body>Login required</body>"));
 }
 [Fact] public void BuildsCorrectPageNumbers()
 {
  var s=new WorkUaSource("c#");
  Assert.Equal("https://www.work.ua/jobs-remote-c%23/",s.ListingUrl(0));
  Assert.Equal("https://www.work.ua/jobs-remote-c%23/?page=2",s.ListingUrl(1));
 }
}
