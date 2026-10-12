using System.Text;

namespace JobRadar.Tests;

/// <summary>Test-only database credentials. Never logs or exports the value.</summary>
internal static class TestDatabaseConnection
{
 private const int MaxSecretBytes = 4096;

 internal static string? Resolve()
 {
  return ReadFromSources(
   Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB_FILE"),
   Environment.GetEnvironmentVariable("JOBRADAR_TEST_DB"));
 }

 internal static string? ReadFromSources(string? secretFilePath, string? legacyValue)
 {
  if(secretFilePath is null)return legacyValue;
  // A configured but empty path is invalid, not a reason to read legacy
  // credentials from an environment variable.
  if(string.IsNullOrWhiteSpace(secretFilePath))
   throw new InvalidOperationException("Test database secret path must be absolute.");
  if(!string.IsNullOrEmpty(legacyValue))
   throw new InvalidOperationException("Use only the test database secret file when JOBRADAR_TEST_DB_FILE is set.");
  if(secretFilePath!=secretFilePath.Trim()||!Path.IsPathFullyQualified(secretFilePath))
   throw new InvalidOperationException("Test database secret path must be absolute.");

  // Defence in depth; the trusted supervisor must supply a read-only,
  // non-symlink secret mount. No arbitrary path can be passed via a job.
  var file=new FileInfo(secretFilePath);
  if(!file.Exists || file.LinkTarget is not null || (file.Attributes&FileAttributes.ReparsePoint)!=0 || (file.Attributes&FileAttributes.Directory)!=0)
   throw new InvalidOperationException("Test database secret must be a regular file.");
  for(var parent=file.Directory;parent is not null;parent=parent.Parent)
  {
   if(parent.LinkTarget is not null || (parent.Attributes&FileAttributes.ReparsePoint)!=0)
    throw new InvalidOperationException("Test database secret path must not contain symlinks.");
  }

  using var stream=new FileStream(secretFilePath,FileMode.Open,FileAccess.Read,
   FileShare.Read,bufferSize:4096,options:FileOptions.SequentialScan);
  if(stream.Length<=0||stream.Length>MaxSecretBytes)
   throw new InvalidOperationException("Test database secret has invalid size.");
  var contents=new byte[(int)stream.Length];
  stream.ReadExactly(contents);
  var value=new UTF8Encoding(false,true).GetString(contents).TrimEnd('\r','\n');
  if(string.IsNullOrWhiteSpace(value)||value.Contains('\0'))
   throw new InvalidOperationException("Test database secret is invalid.");
  return value;
 }
}
