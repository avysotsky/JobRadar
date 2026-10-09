namespace JobRadar;
public interface IJobSource
{
 string Name {get;}
 Uri Home {get;}
 string ListingUrl(int page);
 IReadOnlyList<JobRef> ParseListings(string html);
}
public sealed class DouSource:IJobSource
{
 public string Name=>"dou";
 public Uri Home=>new("https://jobs.dou.ua/");
 // DOU uses AJAX for subsequent pages. Until validated, only first page is fetched and coverage is PARTIAL.
 public string ListingUrl(int page) => page==0 ? "https://jobs.dou.ua/vacancies/?category=.NET&remote=" : throw new NotSupportedException("DOU pagination requires live validation");
 public IReadOnlyList<JobRef> ParseListings(string html)=>Parsers.Listings(Name,html,Home);
}
public sealed class DjinniSource:IJobSource
{
 public string Name=>"djinni";
 public Uri Home=>new("https://djinni.co/");
 public string ListingUrl(int page)=>"https://djinni.co/jobs/?primary_keyword=C%23&employment=remote"+(page>0?$"&page={page+1}":"");
 public IReadOnlyList<JobRef> ParseListings(string html)=>Parsers.Listings(Name,html,Home);
}