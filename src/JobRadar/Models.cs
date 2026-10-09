namespace JobRadar;
public sealed record JobRef(string Source, string Url, string Title, string? Company, DateTimeOffset? PublishedAt);
public sealed record JobDetail(JobRef Job, string Description, bool? Open, DateTimeOffset FetchedAt);
public sealed record SourceResult(string Source, int PagesFetched, int ReferencesFound, int DetailsFetched, int DetailsFailed, int? ReportedTotal, string Status, string? Error);
public sealed record CrawlReport(DateTimeOffset StartedAt, DateTimeOffset EndedAt, IReadOnlyList<SourceResult> Sources);
public sealed class RadarOptions
{
 public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Database=jobradar;Username=jobradar;Password=CHANGE_ME";
 public int MaxPagesPerSource { get; set; } = 12;
 public int DelayMilliseconds { get; set; } = 1200;
 public int TimeoutSeconds { get; set; } = 20;
 public string UserAgent { get; set; } = "JobRadar/0.1 (personal job search; contact: configure-email)";
 public string[] HoursKyiv { get; set; } = ["08:00", "13:00", "19:00"];
 public string OutputDirectory { get; set; } = "reports";
 public bool EnabledDou { get; set; } = true;
 public bool EnabledDjinni { get; set; } = true;
 public bool EnabledRobota { get; set; } = true;
 public string[] RobotaQueries { get; set; } = [".net", "c-sharp", "backend"];
}