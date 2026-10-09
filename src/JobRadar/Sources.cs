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
public sealed class DouSource:IJobSource
{
 public string Name=>"dou";
 public Uri Home=>new("https://jobs.dou.ua/");
 public bool IsSinglePageFeed=>true;
 public string ListingUrl(int page)=>page==0
   ? "https://jobs.dou.ua/vacancies/feeds/?category=.NET&remote=&search=.NET"
   : throw new NotSupportedException("DOU RSS feed has no documented exhaustive pagination");
 public IReadOnlyList<JobRef> ParseListings(string xml)
 {
  var result=new List<JobRef>();
  var document=XDocument.Parse(xml);
  foreach(var item in document.Descendants("item"))
  {
   string? link=(string?)item.Element("link");
   if(!Uri.TryCreate(link,UriKind.Absolute,out var uri) || uri.Host!="jobs.dou.ua")continue;
   var title=Parsers.Clean((string?)item.Element("title"));
   if(title.Length<3)continue;
   DateTimeOffset? published=null;
   if(DateTimeOffset.TryParse((string?)item.Element("pubDate"),out var dt))published=dt;
   result.Add(new JobRef(Name,uri.GetLeftPart(UriPartial.Path),title,null,published));
  }
  return result.DistinctBy(x=>x.Url).ToList();
 }
}
public sealed class DjinniSource:IJobSource
{
 public string Name=>"djinni";
 public Uri Home=>new("https://djinni.co/");
 public string ListingUrl(int page)=>"https://djinni.co/jobs/?primary_keyword=.NET&employment=remote&page="+(page+1);
 public IReadOnlyList<JobRef> ParseListings(string html)=>Parsers.Listings(Name,html,Home);
}