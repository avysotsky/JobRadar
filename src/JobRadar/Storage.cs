using Npgsql;
using NpgsqlTypes;
namespace JobRadar;
public sealed partial class Storage(string connectionString)
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
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS status_checked_at timestamptz;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS status_evidence text;
CREATE TABLE IF NOT EXISTS job_status_checks (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 source text NOT NULL, url text NOT NULL,
 checked_at timestamptz NOT NULL, open_status boolean,
 evidence text NOT NULL,
 FOREIGN KEY(source,url) REFERENCES jobs(source,url) ON DELETE CASCADE);
CREATE INDEX IF NOT EXISTS ix_job_status_checks_latest
 ON job_status_checks(source,url,checked_at DESC);
CREATE TABLE IF NOT EXISTS job_queries (
 source text NOT NULL, url text NOT NULL, query_name text NOT NULL,
 last_seen timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(source,url,query_name),
 FOREIGN KEY(source,url) REFERENCES jobs(source,url) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS job_discoveries (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 source text NOT NULL, url text NOT NULL,
 observed_at timestamptz NOT NULL DEFAULT now(),
 query_name text,
 FOREIGN KEY(source,url) REFERENCES jobs(source,url) ON DELETE CASCADE);
CREATE INDEX IF NOT EXISTS ix_job_discoveries_source_url_observed_at
 ON job_discoveries(source,url,observed_at DESC);
CREATE TABLE IF NOT EXISTS crawl_runs (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, started_at timestamptz NOT NULL,
 ended_at timestamptz NOT NULL, report jsonb NOT NULL);
-- Manual-only tracking of OWN decisions; never imported API descriptions.
CREATE TABLE IF NOT EXISTS project_tracker (
 provider text NOT NULL CHECK(provider IN ('freelancehunt','freelancer')),
 url text NOT NULL, label text,
 own_budget numeric(18,2), own_currency text,
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(provider,url),
 CHECK(length(url)<=1500),
 CHECK(label IS NULL OR length(label)<=180),
 CHECK(own_budget IS NULL OR own_budget>=0),
 CHECK(own_currency IS NULL OR own_currency IN ('USD','EUR','UAH'))
);
CREATE TABLE IF NOT EXISTS project_tracker_events (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 provider text NOT NULL, url text NOT NULL,
 event_type text NOT NULL CHECK(event_type IN
  ('Saved','Applied','Replied','Interview','Won','Rejected','Withdrawn','Closed')),
 event_at timestamptz NOT NULL DEFAULT now(),
 own_note text,
 FOREIGN KEY(provider,url) REFERENCES project_tracker(provider,url) ON DELETE CASCADE,
 CHECK(own_note IS NULL OR length(own_note)<=500)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_project_tracker_applied_once
 ON project_tracker_events(provider,url)
 WHERE event_type='Applied';
CREATE INDEX IF NOT EXISTS ix_project_tracker_events_recent
 ON project_tracker_events(provider,url,event_at DESC,id DESC);
CREATE TABLE IF NOT EXISTS api_request_budget (
 provider text PRIMARY KEY, requests_used int NOT NULL,
 upper_limit int NOT NULL CHECK(upper_limit BETWEEN 1 AND 500));
CREATE TABLE IF NOT EXISTS retry_runs (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, started_at timestamptz NOT NULL,
 ended_at timestamptz NOT NULL, report jsonb NOT NULL);
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
  await using var tx=await c.BeginTransactionAsync(ct);
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
  await using var cmd=new NpgsqlCommand(sql,c,tx);
  cmd.Parameters.AddWithValue("source",job.Source);
  cmd.Parameters.AddWithValue("url",job.Url);
  cmd.Parameters.AddWithValue("title",job.Title);
  cmd.Parameters.AddWithValue("company",(object?)job.Company??DBNull.Value);
  cmd.Parameters.AddWithValue("published",NpgsqlDbType.TimestampTz,(object?)job.PublishedAt?.ToUniversalTime()??DBNull.Value);
  cmd.Parameters.AddWithValue("preview",(object?)job.Preview??DBNull.Value);
  await cmd.ExecuteNonQueryAsync(ct);
  if(!string.IsNullOrWhiteSpace(queryName))
  {
   await using var queryCmd=new NpgsqlCommand("""
INSERT INTO job_queries(source,url,query_name,last_seen)
VALUES(@source,@url,@query,now())
ON CONFLICT(source,url,query_name) DO UPDATE SET last_seen=excluded.last_seen
""",c,tx);
   queryCmd.Parameters.AddWithValue("source",job.Source);
   queryCmd.Parameters.AddWithValue("url",job.Url);
   queryCmd.Parameters.AddWithValue("query",queryName);
   await queryCmd.ExecuteNonQueryAsync(ct);
  }
  // Append exactly one observation per successful discovery invocation.
  // Pre-Phase-15 visits cannot be reconstructed and remain unknown.
  await using(var eventCmd=new NpgsqlCommand("""
INSERT INTO job_discoveries(source,url,query_name)
VALUES(@source,@url,@query)
""",c,tx))
  {
   eventCmd.Parameters.AddWithValue("source",job.Source);
   eventCmd.Parameters.AddWithValue("url",job.Url);
   eventCmd.Parameters.AddWithValue("query",(object?)queryName??DBNull.Value);
   await eventCmd.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
 }
 public async Task Save(JobDetail job,CancellationToken ct)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  const string sql="""
INSERT INTO jobs(source,url,title,company,published_at,description,open_status,last_seen,full_text_at,status_checked_at,status_evidence)
VALUES (@source,@url,@title,@company,@published,@description,@open,@seen,@full,@statuschecked,@evidence)
ON CONFLICT(source,url) DO UPDATE SET title=excluded.title,company=COALESCE(excluded.company,jobs.company),
 published_at=COALESCE(excluded.published_at,jobs.published_at),
 description=CASE WHEN excluded.description<>'' AND
                   (jobs.full_text_at IS NULL OR excluded.full_text_at>=jobs.full_text_at)
                   THEN excluded.description ELSE jobs.description END,
 open_status=CASE WHEN excluded.status_checked_at IS NOT NULL
                    AND (jobs.status_checked_at IS NULL
                      OR excluded.status_checked_at>=jobs.status_checked_at)
                  THEN excluded.open_status
                  WHEN excluded.status_checked_at IS NULL
                  THEN COALESCE(excluded.open_status,jobs.open_status)
                  ELSE jobs.open_status END,
 last_seen=GREATEST(excluded.last_seen,jobs.last_seen),
 full_text_at=GREATEST(excluded.full_text_at,jobs.full_text_at),
 status_checked_at=CASE WHEN excluded.status_checked_at IS NOT NULL
                         AND (jobs.status_checked_at IS NULL
                           OR excluded.status_checked_at>=jobs.status_checked_at)
                         THEN excluded.status_checked_at ELSE jobs.status_checked_at END,
 status_evidence=CASE WHEN excluded.status_checked_at IS NOT NULL
                         AND (jobs.status_checked_at IS NULL
                           OR excluded.status_checked_at>=jobs.status_checked_at)
                      THEN excluded.status_evidence ELSE jobs.status_evidence END;
INSERT INTO job_status_checks(source,url,checked_at,open_status,evidence)
SELECT @source,@url,@seen,@open,@evidence WHERE @evidence IS NOT NULL;
DELETE FROM fetch_errors WHERE source=@source AND url=@url;
""";
  await using var cmd=new NpgsqlCommand(sql,c);
  cmd.Parameters.AddWithValue("source",job.Job.Source);cmd.Parameters.AddWithValue("url",job.Job.Url);
  cmd.Parameters.AddWithValue("title",job.Job.Title);cmd.Parameters.AddWithValue("company",(object?)job.Job.Company??DBNull.Value);
  cmd.Parameters.AddWithValue("published",NpgsqlDbType.TimestampTz,(object?)job.Job.PublishedAt?.ToUniversalTime()??DBNull.Value);
  cmd.Parameters.AddWithValue("description",job.Description);
  // With DBNull.Value Npgsql cannot infer parameter types, particularly in
  // the standalone status-history INSERT ... SELECT ... WHERE expression.
  // Keep types explicit even when the status was not verified by the provider.
  cmd.Parameters.AddWithValue("open",NpgsqlDbType.Boolean,(object?)job.Open??DBNull.Value);
  var fetchedUtc=job.FetchedAt.ToUniversalTime();
  cmd.Parameters.AddWithValue("seen",NpgsqlDbType.TimestampTz,fetchedUtc);
  cmd.Parameters.AddWithValue("full",NpgsqlDbType.TimestampTz,job.Description.Length>0?(object)fetchedUtc:DBNull.Value);
  var observed=!string.IsNullOrWhiteSpace(job.StatusEvidence);
  cmd.Parameters.AddWithValue("statuschecked",NpgsqlDbType.TimestampTz,
    observed?(object)fetchedUtc:DBNull.Value);
  cmd.Parameters.AddWithValue("evidence",NpgsqlDbType.Text,
    observed?(object)job.StatusEvidence!:DBNull.Value);
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
  cmd.Parameters.AddWithValue("s",NpgsqlDbType.TimestampTz,report.StartedAt.ToUniversalTime());
  cmd.Parameters.AddWithValue("e",NpgsqlDbType.TimestampTz,report.EndedAt.ToUniversalTime());
  cmd.Parameters.AddWithValue("r",NpgsqlDbType.Jsonb,System.Text.Json.JsonSerializer.Serialize(report));await cmd.ExecuteNonQueryAsync(ct);
 }
}