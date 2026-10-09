using Npgsql;
using NpgsqlTypes;
namespace JobRadar;
public sealed class Storage(string connectionString)
{
 public async Task Initialize(CancellationToken ct)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  const string sql="""
CREATE TABLE IF NOT EXISTS jobs (
 source text NOT NULL, url text NOT NULL, title text NOT NULL,
 company text, published_at timestamptz, description text NOT NULL DEFAULT '',
 open_status boolean, last_seen timestamptz NOT NULL, full_text_at timestamptz,
 PRIMARY KEY(source,url));
CREATE TABLE IF NOT EXISTS crawl_runs (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, started_at timestamptz NOT NULL,
 ended_at timestamptz NOT NULL, report jsonb NOT NULL);
CREATE TABLE IF NOT EXISTS fetch_errors (
 source text NOT NULL, url text NOT NULL, attempts int NOT NULL DEFAULT 1,
 last_error text NOT NULL, last_attempt timestamptz NOT NULL,
 PRIMARY KEY(source,url));
""";
  await using var cmd=new NpgsqlCommand(sql,c);await cmd.ExecuteNonQueryAsync(ct);
 }
 public async Task Save(JobDetail job,CancellationToken ct)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  const string sql="""
INSERT INTO jobs(source,url,title,company,published_at,description,open_status,last_seen,full_text_at)
VALUES (@source,@url,@title,@company,@published,@description,@open,@seen,@full)
ON CONFLICT(source,url) DO UPDATE SET title=excluded.title,company=COALESCE(excluded.company,jobs.company),
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
 public async Task SaveReport(CrawlReport report,CancellationToken ct)
 {
  await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync(ct);
  await using var cmd=new NpgsqlCommand("INSERT INTO crawl_runs(started_at,ended_at,report) VALUES(@s,@e,@r)",c);
  cmd.Parameters.AddWithValue("s",report.StartedAt);cmd.Parameters.AddWithValue("e",report.EndedAt);
  cmd.Parameters.AddWithValue("r",NpgsqlDbType.Jsonb,System.Text.Json.JsonSerializer.Serialize(report));await cmd.ExecuteNonQueryAsync(ct);
 }
}