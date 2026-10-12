using System.Xml.Linq;
namespace JobRadar;
public interface IJobSource
{
 string Name {get;}
 Uri Home {get;}
 string ListingUrl(int page);
 IReadOnlyList<JobRef> ParseListings(string html);
 // Feed pages cannot support exhaustive coverage without a count reconciliation.
 bool IsSinglePageFeed => false;
 int? ReportedTotal(string payload) => Parsers.ReportedTotal(payload);
}

// Every received RSS <item> must be accounted for. An item can be unusable
// (bad provider link/title) or a duplicate of an earlier item in this feed.
// Query-to-query duplicates remain legitimate discoveries, not RSS losses.
public sealed record RssFeedPage(
 IReadOnlyList<JobRef> Jobs,int RawItems,int UnusableItems,int DuplicateItems)
{
 public int DiscardedItems=>UnusableItems+DuplicateItems;
}

public interface IRssFeedSource:IJobSource
{
 RssFeedPage ParseFeed(string xml);
}

public sealed class DouSource:IRssFeedSource
{
 public string Name=>"dou";
 public Uri Home=>new("https://jobs.dou.ua/");
 public bool IsSinglePageFeed=>true;
 public string ListingUrl(int page)=>page==0
   ? "https://jobs.dou.ua/vacancies/feeds/?category=.NET&remote=&search=.NET"
   : throw new NotSupportedException("DOU RSS feed has no documented exhaustive pagination");
 public IReadOnlyList<JobRef> ParseListings(string xml)=>ParseFeed(xml).Jobs;
 public RssFeedPage ParseFeed(string xml)
 {
  var result=new List<JobRef>();
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var document=XDocument.Parse(xml);
  int raw=0,unusable=0,duplicates=0;
  foreach(var item in document.Descendants("item"))
  {
   raw++;
   string? link=(string?)item.Element("link");
   if(!Uri.TryCreate(link,UriKind.Absolute,out var uri) || uri.Host!="jobs.dou.ua")
   {
    unusable++;
    continue;
   }
   var title=Parsers.Clean((string?)item.Element("title"));
   if(title.Length<3)
   {
    unusable++;
    continue;
   }
   DateTimeOffset? published=null;
   if(DateTimeOffset.TryParse((string?)item.Element("pubDate"),out var dt))published=dt;
   var url=uri.GetLeftPart(UriPartial.Path);
   if(!seen.Add(url))
   {
    duplicates++;
    continue;
   }
   result.Add(new JobRef(Name,url,title,null,published));
  }
  return new RssFeedPage(result,raw,unusable,duplicates);
 }
}
public sealed class DjinniSource:IRssFeedSource
{
 public string Name=>"djinni";
 public Uri Home=>new("https://djinni.co/");
 public bool IsSinglePageFeed=>true;
 // Djinni officially exposes an RSS link from its job search pages.
 // Unlike HTML pagination, a feed is generally capped: coverage MUST remain partial.
 public string ListingUrl(int page)=>page==0
  ? "https://djinni.co/jobs/rss/?primary_keyword=.NET&employment=remote"
  : throw new NotSupportedException("Djinni RSS feed cannot provide exhaustive pagination");
 public IReadOnlyList<JobRef> ParseListings(string xml)=>ParseFeed(xml).Jobs;
 public RssFeedPage ParseFeed(string xml)
 {
  var doc=XDocument.Parse(xml);
  var result=new List<JobRef>();
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  int raw=0,unusable=0,duplicates=0;
  foreach(var item in doc.Descendants("item"))
  {
   raw++;
   var link=(string?)item.Element("link");
   if(!Uri.TryCreate(link,UriKind.Absolute,out var uri) ||
       !(uri.Host=="djinni.co"||uri.Host=="www.djinni.co") ||
       !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath,@"^/jobs/\d+"))
   {
    unusable++;
    continue;
   }
   var title=Parsers.Clean((string?)item.Element("title"));
   if(title.Length<3)
   {
    unusable++;
    continue;
   }
   DateTimeOffset? date=null;
   if(DateTimeOffset.TryParse((string?)item.Element("pubDate"),out var parsed))date=parsed;
   var previewRaw=(string?)item.Element("description")??"";
   var previewDoc=new AngleSharp.Html.Parser.HtmlParser().ParseDocument(previewRaw);
   var preview=Parsers.Clean(previewDoc.Body?.TextContent??previewRaw);
   var url=uri.GetLeftPart(UriPartial.Path);
   if(!seen.Add(url))
   {
    duplicates++;
    continue;
   }
   result.Add(new JobRef(Name,url,title,null,date,
     preview.Length==0?null:preview));
  }
  return new RssFeedPage(result,raw,unusable,duplicates);
 }
}
