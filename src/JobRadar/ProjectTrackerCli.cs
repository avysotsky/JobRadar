namespace JobRadar;

/// <summary>Local-only manual actions. Never contacts freelance platforms.</summary>
public static class ProjectTrackerCli
{
 public static async Task<int> RunAsync(
  Storage storage,string[] args,TextWriter output,TextWriter errors,CancellationToken ct)
 {
  try
  {
   var modes=new[]{"--project-track-add","--project-track-event",
    "--project-track-list","--project-track-delete"};
   var mode=modes.Where(m=>args.Contains(m,StringComparer.OrdinalIgnoreCase)).ToArray();
   if(mode.Length!=1)
    throw new ArgumentException("Exactly one --project-track-* operation required");

   string? value(string key)
   {
    var prefix=key+"=";
    return args.FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
      ?[prefix.Length..];
   }
   string required(string name)=>value(name) is {Length:>0} result
    ? result : throw new ArgumentException("Missing "+name);

   if(mode[0]=="--project-track-list")
   {
    var projects=await storage.ReadTrackedProjectsAsync(ct);
    foreach(var project in projects)
     await output.WriteLineAsync(JsonlOutput.Serialize(project).AsMemory(),ct);
    await errors.WriteLineAsync($"Project tracker: {projects.Count} manually entered references.");
    return 0;
   }
   var provider=required("--project-provider");
   var url=required("--project-url");
   switch(mode[0])
   {
    case "--project-track-add":
    {
     decimal? budget=null;
     if(value("--project-budget") is { } rawBudget)
     {
      if(!decimal.TryParse(rawBudget,System.Globalization.NumberStyles.Number,
          System.Globalization.CultureInfo.InvariantCulture,out var parsed))
       throw new ArgumentException("Invalid --project-budget");
      budget=parsed;
     }
     await storage.SaveManualProjectAsync(provider,url,
      value("--project-label"),budget,value("--project-currency"),ct);
     await output.WriteLineAsync("Manual project reference saved (no provider API call).");
     return 0;
    }
    case "--project-track-event":
     await storage.RecordOwnProjectEventAsync(provider,url,
      required("--project-event"),value("--project-note"),ct);
     await output.WriteLineAsync("User-reported project action recorded (not sent to provider).");
     return 0;
    case "--project-track-delete":
     if(!args.Contains("--confirm-delete",StringComparer.OrdinalIgnoreCase))
      throw new InvalidOperationException("Deletion requires --confirm-delete; associated own events are removed");
     var deleted=await storage.DeleteTrackedProjectAsync(provider,url,ct);
     await output.WriteLineAsync(deleted?"Manual record and events deleted.":"Record not found.");
     return deleted?0:4;
   }
   return 4;
  }
  catch(Exception e) when(e is not OperationCanceledException)
  {
   // Never print arguments, tokens, project notes or confidential URLs.
   await errors.WriteLineAsync("Project tracker: "+e.Message);
   return 4;
  }
 }
}
