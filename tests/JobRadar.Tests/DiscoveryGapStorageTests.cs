using JobRadar;
using Xunit;

namespace JobRadar.Tests;

public sealed class DiscoveryGapStorageTests
{
 [Fact]
 public async Task ReadsDistinctCanonicalDiscoveryEventsAcrossFourProviders()
 {
  var cs=TestDatabaseConnection.Resolve();
  if(string.IsNullOrWhiteSpace(cs))return;
  var storage=new Storage(cs);
  await storage.Initialize(CancellationToken.None);
  var token=Guid.NewGuid().ToString("N");
  var douQuery="dou-rss-phase34-"+token;
  var djinniQuery="djinni-rss-phase34-"+token;
  var robotaQuery="robota-api-phase34-"+token;
  var wwrQuery="wwr-backend";
  var dou=new JobRef("dou","https://jobs.dou.ua/vacancies/"+token+"/",
   "Middle C# Backend",null,null);
  var djinni=new JobRef("djinni","https://djinni.co/jobs/123-"+token+"/",
   "Middle .NET",null,null);
  var robota=new JobRef("robota","https://robota.ua/company12/vacancy"+token,
   "Middle .NET Backend",null,null);
  var wwr=new JobRef("wwr","https://weworkremotely.com/remote-jobs/"+token,
   "Remote C# Backend",null,null);
  var start=DateTimeOffset.UtcNow.AddMinutes(-1);
  await storage.SaveDiscovered(dou,CancellationToken.None,douQuery);
  await storage.SaveDiscovered(dou,CancellationToken.None,douQuery);
  await storage.SaveDiscovered(djinni,CancellationToken.None,djinniQuery);
  await storage.SaveDiscovered(robota,CancellationToken.None,robotaQuery);
  await storage.SaveDiscovered(wwr,CancellationToken.None,wwrQuery);
  var end=DateTimeOffset.UtcNow.AddMinutes(1);
  var names=new[]{douQuery,djinniQuery,robotaQuery,wwrQuery};
  var events=await storage.ReadDiscoveryGapObservationsAsync(
   start,end,names,50000,CancellationToken.None);
  Assert.Single(events,x=>x.Provider=="dou" && x.Query==douQuery && x.Url==dou.Url);
  Assert.Single(events,x=>x.Provider=="djinni" && x.Query==djinniQuery && x.Url==djinni.Url);
  Assert.Single(events,x=>x.Provider=="robota" && x.Query==robotaQuery && x.Url==robota.Url);
  Assert.Single(events,x=>x.Provider=="wwr" && x.Query==wwrQuery && x.Url==wwr.Url);

  // Query-specific isolation is exact even when other PostgreSQL tests
  // concurrently write unrelated vacancy sources and query names.
  var unique=await storage.ReadDiscoveryGapObservationsAsync(
   start,end,[douQuery,djinniQuery,robotaQuery],50000,CancellationToken.None);
  Assert.Equal(3,unique.Count);
  await Assert.ThrowsAsync<InvalidDataException>(()=>
   storage.ReadDiscoveryGapObservationsAsync(
    start,end,[douQuery,djinniQuery,robotaQuery],2,CancellationToken.None));
 }

 [Fact]
 public async Task RejectsInvalidLimitsBeforeConnectingAndIgnoresUnknownNames()
 {
  var storage=new Storage("");
  var start=DateTimeOffset.UtcNow.AddMinutes(-1);
  var end=DateTimeOffset.UtcNow;
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
   storage.ReadDiscoveryGapObservationsAsync(start,end,["dou"],0,CancellationToken.None));
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
   storage.ReadDiscoveryGapObservationsAsync(start,end,["dou"],50001,CancellationToken.None));
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
   storage.ReadDiscoveryGapObservationsAsync(end,start,["dou"],1,CancellationToken.None));
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>
   storage.ReadDiscoveryGapObservationsAsync(start,end,
    Enumerable.Repeat("dou",65).ToArray(),10,CancellationToken.None));
  var empty=await storage.ReadDiscoveryGapObservationsAsync(start,end,
   ["workua-c-sharp","other"],10,CancellationToken.None);
  Assert.Empty(empty);
 }
}
