namespace JobRadar;

/// <summary>
/// In-memory bridge to the existing employment JobRef contract. This is NOT an
/// ingestion service and has no Storage dependency. The WWR RSS terms/retention
/// gate must be resolved before calling Storage.SaveDiscovered.
/// </summary>
public static class WwrEmploymentProjection
{
 public static WwrEmploymentReference Project(RemoteBoardOpening opening)
 {
  if(opening.CanonicalSource!=RemoteBoards.CanonicalWwrSource ||
     opening.Source is not ("wwr-backend" or "wwr-programming" or "wwr-fullstack"))
   throw new ArgumentException("Only validated WWR feed records are supported",nameof(opening));
  if(!Uri.TryCreate(opening.Url,UriKind.Absolute,out var uri) ||
     uri.Scheme!=Uri.UriSchemeHttps ||
     !uri.Host.Equals("weworkremotely.com",StringComparison.OrdinalIgnoreCase) ||
     !uri.AbsolutePath.StartsWith("/remote-jobs/",StringComparison.OrdinalIgnoreCase) ||
     uri.AbsolutePath.Length<="/remote-jobs/".Length ||
     !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
   throw new ArgumentException("WWR reference URL must be canonical HTTPS",nameof(opening));
  return new WwrEmploymentReference(
   new JobRef(RemoteBoards.CanonicalWwrSource,opening.Url,
    opening.Title,opening.Company,opening.PublishedAt,opening.Excerpt),
   opening.FeedQueries.Count>0?opening.FeedQueries:[opening.Source],
   opening.Attribution,opening.ObservedAt,HasFullText:false,OpenStatus:null);
 }
}

public sealed record WwrEmploymentReference(
 JobRef Reference,IReadOnlyList<string> QueryNames,
 string Attribution,DateTimeOffset? ObservedAt,
 bool HasFullText,bool? OpenStatus);
