using System.Net;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class RobotaCoverageEvidenceTests
{
 [Fact]
 public async Task AuthoritativeEmptySearchIsNotAProviderFailure()
 {
  var source=await ScanAsync(_=>Response("""{"total":0,"documents":[]}"""));
  var result=RobotaCoverageEvidence.From(source);
  Assert.Equal("QUERY_RECONCILED",result.Status);
  Assert.Equal(0,result.UniqueReferences);
  Assert.Equal(0,result.RawRecords);
  Assert.Equal(0,result.ReportedTotal);
  Assert.Equal(0,result.UnobservedFromReportedTotal);
  Assert.Contains(RobotaCoverageSignals.VerifiedZero,result.Signals);
  Assert.DoesNotContain(RobotaCoverageSignals.FetchFailed,result.Signals);
  Assert.DoesNotContain(RobotaCoverageSignals.EmptyUnverified,result.Signals);
 }

 [Fact]
 public async Task MissingTotalWithEmptyRecordsIsUnverifiedNotZero()
 {
  var source=await ScanAsync(_=>Response("""{"documents":[]}"""));
  var result=RobotaCoverageEvidence.From(source);
  Assert.Equal("PARTIAL",result.Status);
  Assert.Null(result.ReportedTotal);
  Assert.Null(result.UnobservedFromReportedTotal);
  Assert.Contains(RobotaCoverageSignals.EmptyUnverified,result.Signals);
  Assert.Contains(RobotaCoverageSignals.TotalMissing,result.Signals);
  Assert.DoesNotContain(RobotaCoverageSignals.VerifiedZero,result.Signals);
 }

 [Fact]
 public async Task ContradictoryZeroWithMalformedRawRowIsNotVerified()
 {
  var source=await ScanAsync(_=>Response(
   """{"total":0,"documents":[{"id":0,"notebookId":0,"name":"Malformed"}]}"""));
  var result=RobotaCoverageEvidence.From(source);
  Assert.Equal("PARTIAL",result.Status);
  Assert.Equal(1,result.RawRecords);
  Assert.Equal(1,result.DroppedWithinPages);
  Assert.Contains(RobotaCoverageSignals.RawRecordsDropped,result.Signals);
  Assert.Contains(RobotaCoverageSignals.EmptyUnverified,result.Signals);
  Assert.DoesNotContain(RobotaCoverageSignals.VerifiedZero,result.Signals);
 }

 [Fact]
 public async Task EmptySecondPageIsSeparateFromUnobservedReferences()
 {
  var id=Random.Shared.Next(5000000,9000000);
  var company=Random.Shared.Next(100000,999999);
  var first=JsonSerializer.Serialize(new {total=2,documents=new[]{
   new {id,notebookId=company,name="Middle .NET Backend"}
  }});
  var source=await ScanAsync(uri=>Response(
   uri.Query.Contains("page=0",StringComparison.Ordinal)?first:
   """{"total":2,"documents":[]}"""),maxPages:2);
  var result=RobotaCoverageEvidence.From(source);
  Assert.Equal("PARTIAL",result.Status);
  Assert.Equal(2,result.PagesFetched);
  Assert.Equal(1,result.UniqueReferences);
  Assert.Equal(1,result.UnobservedFromReportedTotal);
  Assert.Contains(RobotaCoverageSignals.EmptyBeforeTotal,result.Signals);
  Assert.Contains(RobotaCoverageSignals.Unobserved,result.Signals);
  Assert.DoesNotContain(RobotaCoverageSignals.VerifiedZero,result.Signals);
 }

 [Fact]
 public async Task PageCapGapIsUnobservedNotAClaimOfVacancyClosure()
 {
  var id=Random.Shared.Next(5000000,9000000);
  var company=Random.Shared.Next(100000,999999);
  var body=JsonSerializer.Serialize(new {total=5,documents=new[]{
   new {id,notebookId=company,name="Middle .NET Backend"}
  }});
  var source=await ScanAsync(_=>Response(body),maxPages:1);
  var result=RobotaCoverageEvidence.From(source);
  Assert.Equal("PARTIAL",result.Status);
  Assert.Equal(4,result.UnobservedFromReportedTotal);
  Assert.Equal(1,result.UniqueReferences);
  Assert.Contains(RobotaCoverageSignals.Unobserved,result.Signals);
  Assert.DoesNotContain(RobotaCoverageSignals.FetchFailed,result.Signals);
 }

 [Fact]
 public async Task ForbiddenSearchIsFetchFailureNotZero()
 {
  var source=await ScanAsync(_=>new HttpResponseMessage(HttpStatusCode.Forbidden));
  var result=RobotaCoverageEvidence.From(source);
  Assert.Equal("FAILED",result.Status);
  Assert.Equal(0,result.PagesFetched);
  Assert.Contains(RobotaCoverageSignals.FetchFailed,result.Signals);
  Assert.DoesNotContain(RobotaCoverageSignals.VerifiedZero,result.Signals);
 }

 [Fact]
 public async Task MissingDocumentsArrayIsParseFailureNotZero()
 {
  var source=await ScanAsync(_=>Response("""{"total":0}"""));
  var result=RobotaCoverageEvidence.From(source);
  Assert.Equal("PARTIAL",result.Status);
  Assert.Equal(1,result.PagesFetched);
  Assert.Contains(RobotaCoverageSignals.ParseFailed,result.Signals);
  Assert.DoesNotContain(RobotaCoverageSignals.VerifiedZero,result.Signals);
 }

 [Fact]
 public void HistoricalReportWithoutRawEvidenceMustNotFabricateVerifiedZero()
 {
  var old=new SourceResult("robota-api-legacy",1,0,0,0,0,
   "QUERY_RECONCILED",null);
  var copy=JsonSerializer.Deserialize<SourceResult>(
   JsonSerializer.Serialize(old))!;
  var evidence=RobotaCoverageEvidence.From(copy);
  Assert.Null(evidence.RawRecords);
  Assert.Null(evidence.DroppedWithinPages);
  Assert.DoesNotContain(RobotaCoverageSignals.VerifiedZero,evidence.Signals);
 }

 [Fact]
 public void HistoricalOverlapAndRawLossAreDistinctDiagnostics()
 {
  var existing=new SourceResult("robota-api-existing",2,2,0,0,4,
   "PARTIAL","Robota API search pages overlap: repeated vacancy IDs across pages",
   RawRecords:5,DroppedRecords:1);
  var evidence=RobotaCoverageEvidence.From(existing);
  Assert.Equal(2,evidence.UnobservedFromReportedTotal);
  Assert.Contains(RobotaCoverageSignals.PageOverlap,evidence.Signals);
  Assert.Contains(RobotaCoverageSignals.RawRecordsDropped,evidence.Signals);
  Assert.Contains(RobotaCoverageSignals.Unobserved,evidence.Signals);
 }

 private static async Task<SourceResult> ScanAsync(
  Func<Uri,HttpResponseMessage> responder,int maxPages=2)
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))
   throw new InvalidOperationException("Robota coverage tests require isolated PostgreSQL");
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  using var http=new HttpClient(new Stub(req=>responder(req.RequestUri!)));
  var options=new RadarOptions
  {
   MaxPagesPerSource=maxPages,
   EnabledRobotaDetails=false
  };
  var name="phase25-"+Guid.NewGuid().ToString("N");
  var report=await new Crawler(new HttpFetcher(http,0),storage,options)
   .RunAsync([new RobotaApiSource(name)],CancellationToken.None);
  return Assert.Single(report.Sources);
 }

 private static HttpResponseMessage Response(string value)=>new(HttpStatusCode.OK)
  {Content=new StringContent(value)};

 private sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> respond)
  :HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(
   HttpRequestMessage request,CancellationToken ct)=>
   Task.FromResult(respond(request));
 }
}
