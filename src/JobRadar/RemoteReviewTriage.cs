using System.Text.RegularExpressions;

namespace JobRadar;

/// <summary>
/// A queue priority for HUMAN review, not an eligibility decision. The base
/// FitBucket stays unchanged, especially for incomplete WWR RSS records.
/// </summary>
public enum RemoteReviewPriority
{
 NotApplicable=0,
 DotNetEvidence=1,
 AdjacentEngineering=2,
 LocationVerification=3,
 UnknownStack=4,
 LowRelevance=5
}

public sealed record RemoteReviewAssessment(
 RemoteReviewPriority Priority,IReadOnlyList<string> Evidence);

public sealed record RemoteReviewBreakdown(
 int TotalNeedsReview,int DotNetEvidence,int AdjacentEngineering,
 int LocationVerification,int UnknownStack,int LowRelevance);

public static class RemoteReviewTriage
{
 private const RegexOptions Flags=RegexOptions.Compiled|
   RegexOptions.IgnoreCase|RegexOptions.CultureInvariant;

 private static readonly Regex Net=new(
  @"(?<![a-z0-9])(?:asp\.net|\.net|dotnet|c#|csharp|c[- ]sharp)(?![a-z0-9])",Flags);

 private static readonly Regex SeniorTitle=new(
  @"\b(?:senior|sr\.?|lead|principal|staff|architect)\b",Flags);

 private static readonly Regex NonTargetTitle=new(
  @"\b(?:project\s+manager|program\s+manager|account\s+manager|product\s+manager|"
  +@"security\s+engineer|security\s+analyst|data\s+scientist|sales|recruiter|"
  +@"content\s+(?:writer|editor)|copywriter|designer|office\s+assistant)\b",Flags);

 private static readonly Regex AdjacentTitle=new(
  @"\b(?:ai\s+agents?|agentic|automation|automations)\b.{0,40}"
  +@"\b(?:engineer|developer|coding|software)\b"
  +@"|\b(?:engineer|developer|software)\b.{0,40}"
  +@"\b(?:ai\s+agents?|agentic|ai\s+coding|automation|automations)\b",Flags);

 private static readonly Regex SoftwareTitle=new(
  @"\b(?:backend|back[- ]end|engineer|developer|programmer|software|"
  +@"systems?\s+development|api)\b",Flags);

 private static readonly Regex ExplicitOtherStack=new(
  @"\b(?:we\s+use|our\s+stack|tech\s+stack|required\s+(?:skills?|experience))"
  +@"\b.{0,100}\b(?:golang|go\s*,\s*typescript|react|typescript|"
  +@"java\b|ruby\s+on\s+rails|node\.?js|swift|kotlin)\b",Flags);

 // A specific "Remote, US"/"Remote (US)" region cue can be used to prioritize
 // verification, NOT to conclude eligibility. A US headquarters alone never
 // means the remote position is US-only.
 private static readonly Regex WwrRemoteRegion=new(
  @"\bremote\s*(?:[-:,]\s*|\(\s*|\s+)(?:the\s+)?"
  +@"(?:united\s+states|usa|us)\b(?:\s*\))?"
  +@"|\b(?:us|usa|united\s+states)\s+remote(?:\s+only)?\b",Flags);

 public static RemoteReviewAssessment Assess(
  RemoteBoardOpening opening,FitBucket bucket)
 {
  if(bucket!=FitBucket.NeedsReview)
   return new RemoteReviewAssessment(RemoteReviewPriority.NotApplicable,[]);

  var title=opening.Title??"";
  var text=opening.FullDescription??opening.Excerpt??"";
  // The Remotive location field is explicit provider metadata. Non-exact
  // regions (including country lists with Ukraine mixed in) require review.
  if(opening.Source=="remotive" &&
     !IsUnrestrictedLocation(opening.CandidateLocation))
   return new RemoteReviewAssessment(RemoteReviewPriority.LocationVerification,
    ["Provider location does not explicitly establish worldwide/Ukraine eligibility"]);

  // WWR RSS commonly begins "Headquarters:". Match only an explicitly stated
  // remote-region cue, rather than treating headquarters as a requirement.
  if(opening.Source.StartsWith("wwr-",StringComparison.Ordinal) &&
     WwrRemoteRegion.IsMatch(text[..Math.Min(600,text.Length)]))
   return new RemoteReviewAssessment(RemoteReviewPriority.LocationVerification,
    ["RSS contains a regional remote-work cue; full posting eligibility requires verification"]);

  if(NonTargetTitle.IsMatch(title))
   return new RemoteReviewAssessment(RemoteReviewPriority.LowRelevance,
    ["Role title belongs to a different specialization than .NET backend"]);

  var netInTitle=Net.IsMatch(title);
  var netInText=Net.IsMatch(text);
  var softwareRole=SoftwareTitle.IsMatch(title);
  if((netInTitle || (softwareRole&&netInText)) &&
     !SeniorTitle.IsMatch(title))
   return new RemoteReviewAssessment(RemoteReviewPriority.DotNetEvidence,
    ["C#/.NET evidence exists; confirm primary stack, seniority and Ukraine eligibility"]);

  if(AdjacentTitle.IsMatch(title) && softwareRole)
   return new RemoteReviewAssessment(RemoteReviewPriority.AdjacentEngineering,
    ["AI-agent/automation development is adjacent to the target backend skillset"]);

  if(!netInTitle && !netInText && ExplicitOtherStack.IsMatch(text))
   return new RemoteReviewAssessment(RemoteReviewPriority.LowRelevance,
    ["Only an alternative primary stack is evident in the received text"]);

  if(softwareRole)
   return new RemoteReviewAssessment(RemoteReviewPriority.UnknownStack,
    ["Engineering role with insufficient evidence of C#/.NET in the received text"]);

  return new RemoteReviewAssessment(RemoteReviewPriority.LowRelevance,
   ["No evidence of the target engineering specialization in the received text"]);
 }

 public static bool IsUnrestrictedLocation(string? location)=>
  location?.Trim().Equals("Worldwide",StringComparison.OrdinalIgnoreCase)==true||
  location?.Trim().Equals("Anywhere",StringComparison.OrdinalIgnoreCase)==true||
  location?.Trim().Equals("Global",StringComparison.OrdinalIgnoreCase)==true||
  location?.Trim().Equals("Ukraine",StringComparison.OrdinalIgnoreCase)==true||
  location?.Trim().Equals("Ukraine only",StringComparison.OrdinalIgnoreCase)==true;

 public static RemoteReviewBreakdown Summarize(
  IEnumerable<RemoteBoardCandidate> candidates)
 {
  var review=candidates.Where(x=>x.Bucket==FitBucket.NeedsReview).ToArray();
  return new RemoteReviewBreakdown(
   review.Length,
   review.Count(x=>x.ReviewPriority==RemoteReviewPriority.DotNetEvidence),
   review.Count(x=>x.ReviewPriority==RemoteReviewPriority.AdjacentEngineering),
   review.Count(x=>x.ReviewPriority==RemoteReviewPriority.LocationVerification),
   review.Count(x=>x.ReviewPriority==RemoteReviewPriority.UnknownStack),
   review.Count(x=>x.ReviewPriority==RemoteReviewPriority.LowRelevance));
 }
}
