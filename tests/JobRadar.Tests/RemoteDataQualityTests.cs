using System.Net;
using System.Text;
using System.Text.Json;
using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class RemoteDataQualityTests
{
 [Fact]
 public void LongWwrRssStillClassifiesLateDotNetAndEnglishConstraints()
 {
  var filler=new string('x',1300);
  var rss=$"""
  <rss version="2.0"><channel><item>
    <title>Middle Backend Engineer</title>
    <link>https://weworkremotely.com/remote-jobs/late-requirement</link>
    <description><![CDATA[<p>Fully remote API development. {filler}
      ASP.NET Core C# .NET required. English B2 required for daily spoken meetings.</p>]]></description>
  </item></channel></rss>
  """;
  var item=Assert.Single(RemoteBoards.ParseWwr(rss,"wwr-programming").Items);
  Assert.False(item.HasFullText);
  Assert.True(item.Excerpt.Length<=951);
  Assert.DoesNotContain("English B2",item.Excerpt);
  Assert.Contains("English B2",item.FullDescription!);
  var assessment=RemoteBoards.Assess(item);
  Assert.Equal(FitBucket.NeedsReview,assessment.Bucket);
  Assert.Contains(assessment.Reasons,x=>x.Contains("C#/.NET"));
  Assert.Contains(assessment.Warnings,x=>x.Contains("B2+"));
  Assert.Contains(assessment.Warnings,x=>x.Contains("not provider-verified"));
  var output=JsonlOutput.Serialize(assessment);
  Assert.DoesNotContain("FullDescription",output);
  Assert.DoesNotContain("English B2 required",output);
 }

 [Fact]
 public async Task RealAuditFailurePatternIsDetectedWithSynthetic43LineFixture()
 {
  // Synthetic reproduction of the owner's Oct 10 live export failure:
  // invalid UTF-8 on 23,30,41 and broken JSON on 27,31,41.
  // Never copy third-party job descriptions into the repository.
  var bytes=new List<byte>();
  for(int line=1;line<=43;line++)
  {
   var row=@"{""Opening"":{""Url"":""https://remotive.com/remote-jobs/example""},""Bucket"":""NeedsReview"",""Note"":""~""}";
   if(line==41)
    row=@"{""Opening"":{""Url"":""https://remotive.com/remote-jobs/example""},""Bucket"":""NeedsReview"",""Note"":""~ ""quoted"" text""}";
   var encoded=Encoding.UTF8.GetBytes(row);
   int marker=Array.IndexOf(encoded,(byte)'~');
   if(marker<0)throw new InvalidOperationException("Marker missing");
   encoded[marker]=line switch
   {
    23=>(byte)0xa2,27=>(byte)0x07,
    30=>(byte)0xfa,31=>(byte)0x1a,
    41=>(byte)0xf1,_=>(byte)'x'
   };
   bytes.AddRange(encoded);
   bytes.Add((byte)'\n');
  }
  var path=Path.Combine(Path.GetTempPath(),"jobradar-corrupt-"+Guid.NewGuid().ToString("N")+".jsonl");
  try
  {
   await File.WriteAllBytesAsync(path,bytes.ToArray());
   var report=await RemoteJsonlIntegrity.CheckAsync(path,CancellationToken.None);
   Assert.Equal(43,report.Lines);
   Assert.False(report.Valid);
   Assert.Equal(3,report.InvalidUtf8);
   Assert.Equal(3,report.InvalidJson);
   Assert.Equal(0,report.InvalidShape);
   Assert.Equal(new[]{23,30,41},report.InvalidUtf8Lines);
   Assert.Equal(new[]{27,31,41},report.InvalidJsonLines);
  }
  finally { if(File.Exists(path))File.Delete(path); }
 }

 [Fact]
 public async Task DirectExportIsValidUtf8JsonlAndPreservesUnicodeWithoutConsoleRedirection()
 {
  const string description="<p>Middle C# ASP.NET Core backend. "+
   "• Сотрудничество → 3± года, résumé “quotes”, Go 0→1, naïve approach.</p>";
  var rss="""
  <rss version="2.0"><channel><item>
   <title>Middle .NET Backend Engineer</title>
   <link>https://weworkremotely.com/remote-jobs/sample-middle-net</link>
   <description><![CDATA[<p>Remote ASP.NET Core PostgreSQL</p>]]></description>
  </item></channel></rss>
  """;
  var remotive=JsonSerializer.Serialize(new{jobs=new[]{
   new{
    url="https://remotive.com/remote-jobs/software-dev/sample-1",
    title="Middle .NET Backend Developer",
    company_name="Über Example",
    candidate_required_location="Worldwide",
    description
   }
  }});
  int calls=0;
  using var http=new HttpClient(new Handler(req=>
  {
   calls++;
   return new HttpResponseMessage(HttpStatusCode.OK)
   {Content=new StringContent(req.RequestUri!.Host=="remotive.com"?remotive:rss,
     Encoding.UTF8,req.RequestUri.Host=="remotive.com"?"application/json":"application/rss+xml")};
  }));
  var folder=Path.Combine(Path.GetTempPath(),"jobradar-utf8-"+Guid.NewGuid().ToString("N"));
  var path=Path.Combine(folder,"remote.jsonl");
  try
  {
   var result=await RemoteBoardFileExport.WriteAsync(http,path,null,CancellationToken.None);
   Assert.Equal(3,calls);
   Assert.Equal(2,result.Report.UniqueJobs);
   Assert.True(result.Integrity.Valid);
   Assert.Equal(2,result.Integrity.Lines);
   var contents=await File.ReadAllBytesAsync(path);
   Assert.NotEmpty(contents);
   Assert.Equal((byte)'{',contents[0]);
   var decoded=new UTF8Encoding(false,true).GetString(contents);
   Assert.Contains("Сотрудничество",decoded);
   Assert.Contains("Über Example",decoded);
   Assert.Contains("“quotes”",decoded);
   Assert.Contains("0→1",decoded);
   Assert.Contains("•",decoded);
   Assert.Contains("naïve",decoded);
   var lines=decoded.Split('\n',StringSplitOptions.RemoveEmptyEntries);
   Assert.Equal(2,lines.Length);
   foreach(var line in lines)
   {
    using var parsed=JsonDocument.Parse(line);
    Assert.True(parsed.RootElement.TryGetProperty("Opening",out _));
   }
   Assert.Equal("PARTIAL",result.Report.CoverageStatus);
  }
  finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
 }

 [Fact]
 public async Task ExistingFileCannotBeOverwrittenOrCauseNetworkCalls()
 {
  var folder=Path.Combine(Path.GetTempPath(),"jobradar-preserve-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(folder);
  var path=Path.Combine(folder,"report.jsonl");
  const string sentinel="owner-previous-report";
  await File.WriteAllTextAsync(path,sentinel);
  int requests=0;
  using var http=new HttpClient(new Handler(_=>
  {
   requests++;
   throw new InvalidOperationException("Should not send");
  }));
  try
  {
   await Assert.ThrowsAsync<IOException>(()=>
    RemoteBoardFileExport.WriteAsync(http,path,null,CancellationToken.None));
   Assert.Equal(0,requests);
   Assert.Equal(sentinel,await File.ReadAllTextAsync(path));
  }
  finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
 }

 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> responder):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)=>
   Task.FromResult(responder(req));
 }
}
