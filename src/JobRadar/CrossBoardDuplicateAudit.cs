using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>
/// Evidence of two separately saved job-board postings with near-identical
/// FULL descriptions. This is only a possible duplicate, never an instruction
/// to discard a URL or carry an Applied decision to the other board.
/// </summary>
public sealed record CrossBoardDuplicatePosting(
 string Source,string Url,string Title,string? Company,
 DateTimeOffset LastSeen,bool? IsOpen,string? OwnerDecision);

public sealed record CrossBoardDuplicatePair(
 CrossBoardDuplicatePosting First,CrossBoardDuplicatePosting Second,
 double TextSimilarity,string Evidence);

public sealed record CrossBoardDuplicateReport(
 DateTimeOffset EvaluatedAt,int Evaluated,long TotalStored,long OmittedBySnapshotLimit,
 int FullTextCandidates,int MatchedPairs,int ReturnedPairs,int OmittedPairs,
 IReadOnlyList<CrossBoardDuplicatePair> Pairs,string Interpretation);

/// <summary>
/// Phase 39. Conservative offline cross-board *candidate* duplicate audit.
/// DOU/Djinni only: both boards currently supply full text. Robota preview
/// and WWR RSS excerpt cannot satisfy this test and must not be treated as
/// evidence that there are no duplicates on those platforms.
/// </summary>
public static class CrossBoardDuplicateAudit
{
 public const int MaxInput=5000;
 public const int MaxOutput=100;
 private const int MinContentLength=450;
 private const int PrefixLength=70;
 private const int ShingleLength=12;
 private const double MinimumSimilarity=0.96;
 private static readonly Regex StrimcoStyleTitleSuffix=new(
  @"\s+(?:в|у|at)\s+.+$",RegexOptions.IgnoreCase|
  RegexOptions.CultureInvariant|RegexOptions.Compiled);
 private static readonly Regex NonAlphanumeric=new(
  @"[^\p{L}\p{N}]+",RegexOptions.CultureInvariant|RegexOptions.Compiled);

 private sealed record Candidate(
  VacancySnapshot Snapshot,string Title,string Normalized,
  string Prefix,HashSet<string> Shingles);

 public static CrossBoardDuplicateReport Analyze(
  IReadOnlyList<VacancySnapshot> vacancies,
  IReadOnlyDictionary<string,EmploymentDecision> decisions,
  long totalStored,DateTimeOffset now)
 {
  ArgumentNullException.ThrowIfNull(vacancies);
  ArgumentNullException.ThrowIfNull(decisions);
  if(vacancies.Count>MaxInput)
   throw new ArgumentOutOfRangeException(nameof(vacancies));
  if(totalStored<vacancies.Count)
   throw new ArgumentOutOfRangeException(nameof(totalStored));
  var candidates=new List<Candidate>();
  var identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var item in vacancies)
  {
   if(!identities.Add(item.Source+"\u001f"+item.Url))
    throw new InvalidDataException("Repeated canonical job in duplicate audit");
   if(!item.HasFullText || string.IsNullOrWhiteSpace(item.Description) ||
      !StatusOnlyRefresh.IsApprovedProviderUrl(item.Source,item.Url))
    continue;
   var canonical=Normalize(item.Description);
   if(canonical.Length<MinContentLength)continue;
   var title=Normalize(StrimcoStyleTitleSuffix.Replace(item.Title,""));
   if(title.Length<8)continue;
   var prefix=canonical[..PrefixLength];
   candidates.Add(new Candidate(item,title,canonical,prefix,
    CharacterShingles(canonical)));
  }

  var pairs=new List<CrossBoardDuplicatePair>();
  // Only compare vacancies with the same core job title AND the same long
  // opening passage; a shared company or generic .NET title is not enough.
  foreach(var group in candidates.GroupBy(x=>(x.Title,x.Prefix)))
  {
   var rows=group.OrderBy(x=>x.Snapshot.Source,StringComparer.Ordinal)
    .ThenBy(x=>x.Snapshot.Url,StringComparer.Ordinal).ToArray();
   if(rows.Length>200)
    throw new InvalidDataException("Duplicate candidate group is unexpectedly large");
   for(int i=0;i<rows.Length;i++)
   for(int j=i+1;j<rows.Length;j++)
   {
    var a=rows[i];var b=rows[j];
    if(a.Snapshot.Source.Equals(b.Snapshot.Source,StringComparison.OrdinalIgnoreCase))
     continue;
    var min=Math.Min(a.Normalized.Length,b.Normalized.Length);
    var max=Math.Max(a.Normalized.Length,b.Normalized.Length);
    if(min<(double)max*0.85)continue;
    var score=DiceCoefficient(a.Shingles,b.Shingles);
    if(score<MinimumSimilarity)continue;
    pairs.Add(new CrossBoardDuplicatePair(
     Posting(a.Snapshot,decisions),Posting(b.Snapshot,decisions),
     Math.Round(score,4,MidpointRounding.AwayFromZero),
     "POSSIBLE_DUPLICATE: independent board URLs; equal normalized core "+
     "title; same first 70 normalized description characters; >=96% "+
     "unique 12-character shingle Dice similarity in complete descriptions. "+
     "Review both links manually; never inherit employment actions."));
   }
  }
  var ordered=pairs.OrderByDescending(x=>x.TextSimilarity)
   .ThenBy(x=>x.First.Source,StringComparer.Ordinal)
   .ThenBy(x=>x.First.Url,StringComparer.Ordinal)
   .ThenBy(x=>x.Second.Url,StringComparer.Ordinal).ToArray();
  return new CrossBoardDuplicateReport(now,vacancies.Count,totalStored,
   totalStored-vacancies.Count,candidates.Count,ordered.Length,
   Math.Min(MaxOutput,ordered.Length),Math.Max(0,ordered.Length-MaxOutput),
   ordered.Take(MaxOutput).ToArray(),
   "Possible cross-board duplicates based on near-identical COMPLETE "+
   "description text, never on titles, companies, or posting dates alone. "+
   "Pairs are not clusters and may share a posting. Status and Applied/"+
   "Inactive decisions remain URL-specific; no jobs are modified, hidden "+
   "or automatically marked Applied. This audit only evaluates DOU and "+
   "Djinni full texts. Robota preview-only, WWR RSS-only and omitted "+
   "database records remain outside evidence; zero pairs does not "+
   "demonstrate complete cross-board deduplication.");
 }

 private static CrossBoardDuplicatePosting Posting(
  VacancySnapshot item,IReadOnlyDictionary<string,EmploymentDecision> decisions)
 {
  string? action=null;
  if(Storage.IsAcceptableEmploymentUrl(item.Url) &&
     decisions.TryGetValue(Storage.CanonicalEmploymentUrl(item.Url),out var saved))
   action=saved.Decision;
  return new(item.Source,item.Url,item.Title,item.Company,
   item.LastSeen,item.IsOpen,action);
 }

 private static string Normalize(string s)=>
  NonAlphanumeric.Replace(s.ToLowerInvariant(),"");

 private static HashSet<string> CharacterShingles(string normalized)
 {
  var result=new HashSet<string>(StringComparer.Ordinal);
  for(int i=0;i+ShingleLength<=normalized.Length;i++)
   result.Add(normalized.Substring(i,ShingleLength));
  return result;
 }

 private static double DiceCoefficient(HashSet<string> left,HashSet<string> right)
 {
  if(left.Count==0 || right.Count==0)return 0;
  int common=left.Count<=right.Count?
   left.Count(right.Contains):right.Count(left.Contains);
  return 2.0*common/(left.Count+right.Count);
 }
}
