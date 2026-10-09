using Npgsql;
using NpgsqlTypes;
namespace JobRadar;
public sealed class Storage(string connectionString)
{
 public async Task Initialize(CancellationToken ct)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  await using var tx=await c.BeginTransactionAsync(ct);
  // Serialize schema initialization across parallel workers and test processes.
  await using(var lockCmd=new NpgsqlCommand("SELECT pg_advisory_xact_lock(872349217)",c,tx))
   await lockCmd.ExecuteNonQueryAsync(ct);
  const string sql="""
CREATE TABLE IF NOT EXISTS jobs (
 source text NOT NULL, url text NOT NULL, title text NOT NULL,
 company text, published_at timestamptz, description text NOT NULL DEFAULT '',
 open_status boolean, last_seen timestamptz NOT NULL, full_text_at timestamptz,
 PRIMARY KEY(source,url));
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS preview text;
CREATE TABLE IF NOT EXISTS job_queries (
 source text NOT NULL, url text NOT NULL, query_name text NOT NULL,
 last_seen timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(source,url,query_name),
 FOREIGN KEY(source,url) REFERENCES jobs(source,url) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS crawl_runs (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, started_at timestamptz NOT NULL,
 ended_at timestamptz NOT NULL, report jsonb NOT NULL);
CREATE TABLE IF NOT EXISTS api_request_budget (
 provider text PRIMARY KEY, requests_used int NOT NULL,
 upper_limit int NOT NULL CHECK(upper_limit BETWEEN 1 AND 500));
CREATE TABLE IF NOT EXISTS fetch_errors (
 source text NOT NULL, url text NOT NULL, attempts int NOT NULL DEFAULT 1,
 last_error text NOT NULL, last_attempt timestamptz NOT NULL,
 PRIMARY KEY(source,url));
""";
  await using var cmd=new NpgsqlCommand(sql,c,tx);await cmd.ExecuteNonQueryAsync(ct);
  await tx.CommitAsync(ct);
 }
 public async Task SaveDiscovered(JobRef job,CancellationToken ct,string? queryName=null)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  const string sql="""
INSERT INTO jobs(source,url,title,company,published_at,preview,last_seen)
VALUES(@source,@url,@title,@company,@published,@preview,now())
ON CONFLICT(source,url) DO UPDATE SET
 title=excluded.title,
 company=COALESCE(excluded.company,jobs.company),
 published_at=COALESCE(excluded.published_at,jobs.published_at),
 preview=COALESCE(excluded.preview,jobs.preview),
 last_seen=excluded.last_seen;
""";
  await using var cmd=new NpgsqlCommand(sql,c);
  cmd.Parameters.AddWithValue("source",job.Source);
  cmd.Parameters.AddWithValue("url",job.Url);
  cmd.Parameters.AddWithValue("title",job.Title);
  cmd.Parameters.AddWithValue("company",(object?)job.Company??DBNull.Value);
  cmd.Parameters.AddWithValue("published",NpgsqlDbType.TimestampTz,(object?)job.PublishedAt??DBNull.Value);
  cmd.Parameters.AddWithValue("preview",(object?)job.Preview??DBNull.Value);
  await cmd.ExecuteNonQueryAsync(ct);
  if(!string.IsNullOrWhiteSpace(queryName))
  {
   await using var queryCmd=new NpgsqlCommand("""
INSERT INTO job_queries(source,url,query_name,last_seen)
VALUES(@source,@url,@query,now())
ON CONFLICT(source,url,query_name) DO UPDATE SET last_seen=excluded.last_seen
""",c);
   queryCmd.Parameters.AddWithValue("source",job.Source);
   queryCmd.Parameters.AddWithValue("url",job.Url);
   queryCmd.Parameters.AddWithValue("query",queryName);
   await queryCmd.ExecuteNonQueryAsync(ct);
  }
 }
 public async Task Save(JobDetail job,CancellationToken ct)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  const string sql="""
INSERT INTO jobs(source,url,title,company,published_at,description,open_status,last_seen,full_text_at)
VALUES (@source,@url,@title,@company,@published,@description,@open,@seen,@full)
ON CONFLICT(source,url) DO UPDATE SET title=excluded.title,company=COALESCE(excluded.company,jobs.company),
 published_at=COALESCE(excluded.published_at,jobs.published_at),
 description=CASE WHEN excluded.description<>'' THEN excluded.description ELSE jobs.description END,
 open_status=COALESCE(excluded.open_status,jobs.open_status),last_seen=excluded.last_seen,
 full_text_at=COALESCE(excluded.full_text_at,jobs.full_text_at);
DELETE FROM fetch_errors WHERE source=@source AND url=@url;
""";
  await using var cmd=new NpgsqlCommand(sql,c);
  cmd.Parameters.AddWithValue("source",job.Job.Source);cmd.Parameters.AddWithValue("url",job.Job.Url);
  cmd.Parameters.AddWithValue("title",job.Job.Title);cmd.Parameters.AddWithValue("company",(object?)job.Job.Company??DBNull.Value);
  cmd.Parameters.AddWithValue("published",NpgsqlDbType.TimestampTz,(object?)job.Job.PublishedAt??DBNull.Value);
  cmd.Parameters.AddWithValue("description",job.Description);
  cmd.Parameters.AddWithValue("open",(object?)job.Open??DBNull.Value);
  cmd.Parameters.AddWithValue("seen",job.FetchedAt);
  cmd.Parameters.AddWithValue("full",NpgsqlDbType.TimestampTz,job.Description.Length>0?(object)job.FetchedAt:DBNull.Value);
  await cmd.ExecuteNonQueryAsync(ct);
 }
 public async Task SaveError(string source,string url,string error,CancellationToken ct)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  const string sql="""INSERT INTO fetch_errors(source,url,last_error,last_attempt) VALUES(@s,@u,@e,now()) ON CONFLICT(source,url) DO UPDATE SET attempts=fetch_errors.attempts+1,last_error=excluded.last_error,last_attempt=excluded.last_attempt""";
  await using var cmd=new NpgsqlCommand(sql,c);cmd.Parameters.AddWithValue("s",source);cmd.Parameters.AddWithValue("u",url);cmd.Parameters.AddWithValue("e",error);await cmd.ExecuteNonQueryAsync(ct);
 }
 /// <summary>
 /// Atomically reserves one lifetime call BEFORE an HTTP request is attempted.
 /// 'alreadyUsed' must conservatively include requests spent outside JobRadar.
 /// Consumes the reservation even if a request fails (fail closed).
 /// </summary>
 public async Task<int> ReserveJoobleCallAsync(int alreadyUsed,int allowedNew,CancellationToken ct,
                                               string provider="jooble-ua")
 {
  if(alreadyUsed is <0 or >500 || allowedNew is <1 or >500 || string.IsNullOrWhiteSpace(provider))
   throw new ArgumentOutOfRangeException(nameof(allowedNew));
  await using var c=new NpgsqlConnection(connectionString);
  await c.OpenAsync(ct);
  const string sql="""
INSERT INTO api_request_budget(provider,requests_used,upper_limit)
SELECT @p,@prior+1,LEAST(500,@prior+@allow)
WHERE @prior+1<=LEAST(500,@prior+@allow)
ON CONFLICT(provider) DO UPDATE SET
 requests_used=GREATEST(api_request_budget.requests_used,@prior)+1,
 upper_limit=LEAST(api_request_budget.upper_limit,500,@prior+@allow)
WHERE GREATEST(api_request_budget.requests_used,@prior) <
      LEAST(api_request_budget.upper_limit,500,@prior+@allow)
RETURNING requests_used;
""";
  await using var cmd=new NpgsqlCommand(sql,c);
  cmd.Parameters.AddWithValue("p",provider);
  cmd.Parameters.AddWithValue("prior",alreadyUsed);
  cmd.Parameters.AddWithValue("allow",allowedNew);
  var value=await cmd.ExecuteScalarAsync(ct);
  if(value is not int used)
   throw new InvalidOperationException("Jooble lifetime quota exhausted or blocked");
  return used;
 }

 public async Task SaveReport(CrawlReport report,CancellationToken ct)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand("INSERT INTO crawl_runs(started_at,ended_at,report) VALUES(@s,@e,@r)",c);
  cmd.Parameters.AddWithValue("s",report.StartedAt);cmd.Parameters.AddWithValue("e",report.EndedAt);
  cmd.Parameters.AddWithValue("r",NpgsqlDbType.Jsonb,System.Text.Json.JsonSerializer.Serialize(report));await cmd.ExecuteNonQueryAsync(ct);
 }
}