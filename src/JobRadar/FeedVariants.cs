namespace JobRadar;

/// <summary>
/// Alternate public RSS searches complement the default .NET category feed.
/// Each result retains its canonical DOU/Djinni source key, while Name records
/// the filter that discovered it. RSS caps are never treated as exhaustive.
/// </summary>
public sealed class DouKeywordFeed(string keyword) : IJobSource
{
 public string Name=>"dou-rss-"+keyword;
 public Uri Home=>new("https://jobs.dou.ua/");
 public bool IsSinglePageFeed=>true;
 public string ListingUrl(int page)=>page==0
    ? "https://jobs.dou.ua/vacancies/feeds/?remote=&search="+Uri.EscapeDataString(keyword)
    : throw new NotSupportedException("DOU RSS does not expose complete paging");
 public IReadOnlyList<JobRef> ParseListings(string payload)=>
  new DouSource().ParseListings(payload).Select(x=>x with {Source="dou"}).ToArray();
}

public sealed class DjinniKeywordFeed(string keyword) : IJobSource
{
 public string Name=>"djinni-rss-"+keyword;
 public Uri Home=>new("https://djinni.co/");
 public bool IsSinglePageFeed=>true;
 public string ListingUrl(int page)=>page==0
    ? "https://djinni.co/jobs/rss/?employment=remote&keywords="+Uri.EscapeDataString(keyword)
    : throw new NotSupportedException("Djinni RSS does not expose complete paging");
 public IReadOnlyList<JobRef> ParseListings(string payload)=>
  new DjinniSource().ParseListings(payload).Select(x=>x with {Source="djinni"}).ToArray();
}

public static class FeedVariants
{
 public static IReadOnlyList<IJobSource> Create(RadarOptions options)
 {
  var sources=new List<IJobSource>();
  if(options.EnabledDou)
  {
   sources.Add(new DouSource());
   foreach(var term in ValidTerms(options.DouExtraKeywords))
    sources.Add(new DouKeywordFeed(term));
  }
  if(options.EnabledDjinni)
  {
   sources.Add(new DjinniSource());
   foreach(var term in ValidTerms(options.DjinniExtraKeywords))
    sources.Add(new DjinniKeywordFeed(term));
  }
  return sources;
 }

 private static IEnumerable<string> ValidTerms(IEnumerable<string>? keywords)
  => (keywords??[]).Where(x=>!string.IsNullOrWhiteSpace(x))
    .Select(x=>x.Trim()).Where(x=>x.Length<=60)
    .Distinct(StringComparer.OrdinalIgnoreCase);
}
