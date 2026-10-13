namespace JobRadar;

/// <summary>
/// The published WWR RSS categories extend the ordinary employment discovery
/// pipeline. They provide a bounded feed excerpt, NOT complete provider detail,
/// proof that applications remain open, or Ukraine work eligibility.
/// WWR URLs are kept as direct original-provider links for attribution.
/// </summary>
public sealed class WwrRssSource(string category) : IRssFeedSource
{
 public string Name=>category switch
 {
  "backend"=>"wwr-backend",
  "programming"=>"wwr-programming",
  _=>throw new ArgumentOutOfRangeException(nameof(category))
 };
 public Uri Home=>new("https://weworkremotely.com/");
 public bool IsSinglePageFeed=>true;
 public string ListingUrl(int page)
 {
  if(page!=0)throw new NotSupportedException(
   "WWR RSS exposes a bounded category feed without guaranteed exhaustive paging");
  return category switch
  {
   "backend"=>RemoteBoards.WwrBackend,
   "programming"=>RemoteBoards.WwrProgramming,
   _=>throw new ArgumentOutOfRangeException(nameof(category))
  };
 }
 public IReadOnlyList<JobRef> ParseListings(string payload)=>ParseFeed(payload).Jobs;
 public RssFeedPage ParseFeed(string payload)
 {
  var page=RemoteBoards.ParseWwr(payload,Name);
  var jobs=new List<JobRef>();
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  int additionalUnusable=0,duplicates=0;
  foreach(var vacancy in page.Items)
  {
   // Defence in depth: no credential-bearing URLs even if a parser is ever
   // extended to allow a different link format.
   if(!Uri.TryCreate(vacancy.Url,UriKind.Absolute,out var uri)||
      uri.Scheme!=Uri.UriSchemeHttps||uri.UserInfo.Length>0||
      !(uri.Host=="weworkremotely.com"||uri.Host=="www.weworkremotely.com"))
   {
    additionalUnusable++;
    continue;
   }
   var canonical=uri.GetLeftPart(UriPartial.Path);
   if(!seen.Add(canonical)){duplicates++;continue;}
   jobs.Add(new JobRef("wwr",canonical,vacancy.Title,vacancy.Company,
     vacancy.PublishedAt,string.IsNullOrWhiteSpace(vacancy.Excerpt)?null:vacancy.Excerpt));
  }
  return new RssFeedPage(jobs,page.RawCount,
   page.DroppedCount+additionalUnusable,duplicates);
 }
}
