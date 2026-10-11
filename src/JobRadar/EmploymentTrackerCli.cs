namespace JobRadar;

/// <summary>Explicit manual employment tracking, never a provider submission.</summary>
public static class EmploymentTrackerCli
{
 public static async Task<int> RunAsync(
  Storage store,string[] args,TextWriter output,TextWriter errors,CancellationToken ct)
 {
  try
  {
   var modes=new[]{"--employment-track-add","--employment-track-event",
                   "--employment-track-list","--employment-track-delete"};
   var chosen=modes.Where(m=>args.Contains(m,StringComparer.OrdinalIgnoreCase)).ToArray();
   if(chosen.Length!=1)throw new ArgumentException("Exactly one employment-track operation required");
   string? value(string key)
   {
    var prefix=key+"=";
    var item=args.FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
    return item is null?null:item[prefix.Length..];
   }
   string required(string key)=>value(key) is {Length:>0} v
      ?v:throw new ArgumentException("Missing "+key);
   if(chosen[0]=="--employment-track-list")
   {
    var items=await store.ReadTrackedEmploymentAsync(ct);
    foreach(var item in items)
     await output.WriteLineAsync(JsonlOutput.Serialize(item).AsMemory(),ct);
    await errors.WriteLineAsync($"Employment tracker: {items.Count} owner-entered records.");
    return 0;
   }
   var url=required("--employment-url");
   switch(chosen[0])
   {
    case "--employment-track-add":
     await store.SaveManualEmploymentAsync(url,value("--employment-label"),ct);
     await output.WriteLineAsync("Employment reference saved locally; no application sent.");
     return 0;
    case "--employment-track-event":
     await store.RecordOwnEmploymentEventAsync(url,required("--employment-event"),
       value("--employment-note"),ct);
     await output.WriteLineAsync("Owner-reported employment event saved locally; no application sent.");
     return 0;
    case "--employment-track-delete":
     if(!args.Contains("--confirm-delete",StringComparer.OrdinalIgnoreCase))
      throw new InvalidOperationException("Deletion requires --confirm-delete");
     var deleted=await store.DeleteTrackedEmploymentAsync(url,ct);
     await output.WriteLineAsync(deleted?"Employment record deleted.":"Record not found.");
     return deleted?0:4;
   }
   return 4;
  }
  catch(Exception e) when(e is not OperationCanceledException)
  {
   // Do not print argument arrays or private notes. These errors are local-only.
   await errors.WriteLineAsync("Employment tracker: "+e.Message);
   return 4;
  }
 }
}
