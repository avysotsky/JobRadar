using System.Text;
using System.Text.Json;

namespace JobRadar;

/// <summary>
/// Strict UTF-8 + structured JSONL validation. Counts encoding failures and
/// JSON syntax failures independently, even when both affect the same line.
/// </summary>
public static class RemoteJsonlIntegrity
{
 public const long MaxAuditBytes=32L*1024*1024;
 private static readonly UTF8Encoding StrictUtf8=new(false,true);

 public static async Task<RemoteJsonlIntegrityReport> CheckAsync(
  string path,CancellationToken ct)
 {
  var info=new FileInfo(path);
  if(info.Length>MaxAuditBytes)
   throw new InvalidDataException("Remote JSONL exceeds the 32 MiB audit limit");
  var bytes=await File.ReadAllBytesAsync(path,ct);
  int rows=0,invalidUtf8=0,invalidJson=0,invalidShape=0;
  var badUtf8=new List<int>();
  var badJson=new List<int>();
  var badShape=new List<int>();
  int number=0;
  for(int start=0;start<bytes.Length;)
  {
   ct.ThrowIfCancellationRequested();
   int ending=Array.IndexOf(bytes,(byte)'\n',start);
   if(ending<0)ending=bytes.Length;
   number++;
   int length=ending-start;
   if(length>0&&bytes[start+length-1]=='\r')length--;
   if(length>0)
   {
    rows++;
    string text;
    try
    {
     text=StrictUtf8.GetString(bytes,start,length);
    }
    catch(DecoderFallbackException)
    {
     invalidUtf8++;badUtf8.Add(number);
     // Diagnostic only: replacement decoding helps detect additional JSON
     // damage on a line that also contains invalid UTF-8 bytes.
     text=Encoding.UTF8.GetString(bytes,start,length);
    }
    try
    {
     using var document=JsonDocument.Parse(text);
     var root=document.RootElement;
     if(root.ValueKind!=JsonValueKind.Object||
        !root.TryGetProperty("Opening",out var opening)||
        opening.ValueKind!=JsonValueKind.Object||
        !opening.TryGetProperty("Url",out var url)||
        url.ValueKind!=JsonValueKind.String||
        !root.TryGetProperty("Bucket",out var bucket)||
        bucket.ValueKind!=JsonValueKind.String)
     {
      invalidShape++;badShape.Add(number);
     }
    }
    catch(JsonException)
    {
     invalidJson++;badJson.Add(number);
    }
   }
   if(ending==bytes.Length)break;
   start=ending+1;
  }
  return new RemoteJsonlIntegrityReport(rows,invalidUtf8,invalidJson,invalidShape,
   badUtf8,badJson,badShape);
 }
}

public sealed record RemoteJsonlIntegrityReport(
 int Lines,int InvalidUtf8,int InvalidJson,int InvalidShape,
 IReadOnlyList<int> InvalidUtf8Lines,IReadOnlyList<int> InvalidJsonLines,
 IReadOnlyList<int> InvalidShapeLines)
{
 public bool Valid=>InvalidUtf8==0&&InvalidJson==0&&InvalidShape==0;
}

/// <summary>
/// Output is written directly to an isolated UTF-8 temp file, verified before
/// publication, then moved to a previously unused destination filename.
/// It cannot be corrupted by PowerShell/native console codepage redirection.
/// </summary>
public static class RemoteBoardFileExport
{
 public static async Task<RemoteBoardFileResult> WriteAsync(
  HttpClient http,string path,Action<string>? progress,CancellationToken ct,
  bool wwrOnly=false,bool includeFullstack=false)
 {
  if(string.IsNullOrWhiteSpace(path)||
     !path.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase))
   throw new ArgumentException("Remote boards output path must end with .jsonl");
  var destination=Path.GetFullPath(path);
  if(File.Exists(destination))
   throw new IOException("Output already exists; choose a new filename");
  var directory=Path.GetDirectoryName(destination)!;
  Directory.CreateDirectory(directory);
  var temp=Path.Combine(directory,"."+Path.GetFileName(destination)+
   "."+Guid.NewGuid().ToString("N")+".tmp");
  try
  {
   RemoteBoardsReport report;
   await using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,
       FileShare.None,65536,FileOptions.Asynchronous))
   await using(var writer=new StreamWriter(stream,new UTF8Encoding(false,true)))
   {
    report=wwrOnly
     ?await RemoteBoards.ScanWwrAsync(http,writer,progress,ct,includeFullstack)
     :await RemoteBoards.ScanAsync(http,writer,progress,ct);
    await writer.FlushAsync(ct);
   }
   var integrity=await RemoteJsonlIntegrity.CheckAsync(temp,ct);
   if(!integrity.Valid||integrity.Lines!=report.UniqueJobs)
    throw new InvalidDataException(
     "Remote export integrity validation failed; incomplete output was not published");
   // File.Move without overwrite: never clobber the owner's previous reports.
   File.Move(temp,destination);
   return new RemoteBoardFileResult(destination,report,integrity);
  }
  finally
  {
   if(File.Exists(temp))File.Delete(temp);
  }
 }
}

public sealed record RemoteBoardFileResult(
 string Path,RemoteBoardsReport Report,RemoteJsonlIntegrityReport Integrity);
