using System.Text.Json;
namespace TagPrinter;

/// <summary>Operational backup metadata; it is outside SQLite so a restore cannot rewrite the current machine's status.</summary>
internal sealed record BackupSnapshot
{
    /// <summary>UTC completion time of the latest recorded successful consistent snapshot.</summary>
    public string LastSuccessUtc {get;init;}="";
    /// <summary>Absolute destination of that successful snapshot; its continued existence is checked separately.</summary>
    public string LastPath {get;init;}="";
    /// <summary>UTC time of the latest success or failure recorded by the backup coordinator.</summary>
    public string LastAttemptUtc {get;init;}="";
    /// <summary>Latest failure detail; a later successful backup clears this value.</summary>
    public string Error {get;init;}="";
}
/// <summary>Persists best-effort status beside each database without changing the result of a successful SQLite backup.</summary>
internal static class BackupStatus
{
    // Concurrent automatic/manual operations serialize the small status replacement, never SQLite work itself.
    private static readonly object gate=new();
    private static string PathFor(string database)=>database+".backup-status.json";
    /// <summary>Reads durable metadata or reports unreadable metadata explicitly; no inventory is overwritten.</summary>
    internal static BackupSnapshot Read(string database)
    {
        string path=PathFor(database);if(!File.Exists(path))return new();
        try
        {
            if(new FileInfo(path).Length>65536)throw new InvalidDataException("Backup status is too large.");
            var value=JsonSerializer.Deserialize<BackupSnapshot>(File.ReadAllText(path))??throw new InvalidDataException("Empty backup status.");
            if(value.LastSuccessUtc is null || value.LastPath is null || value.LastAttemptUtc is null || value.Error is null || value.LastPath.Length>4096 || value.Error.Length>8192)throw new InvalidDataException("Invalid backup status values.");
            foreach(string timestamp in new[]{value.LastSuccessUtc,value.LastAttemptUtc})if(timestamp.Length>0 && !DateTimeOffset.TryParse(timestamp,out _))throw new InvalidDataException("Invalid backup status date.");
            return value;
        }
        catch(Exception e){return new(){Error=Ui.T("No se pudo leer el estado: ","Could not read backup status: ")+e.Message};}
    }
    /// <summary>Records success or failure while retaining the prior successful path on a failed attempt.</summary>
    internal static void Record(string database,string target,Exception? failure=null)
    {
        lock(gate)
        {
            string temp=PathFor(database)+"."+Guid.NewGuid()+".tmp";
            try
            {
                var before=Read(database);string now=DateTimeOffset.UtcNow.ToString("O");var next=failure is null?new BackupSnapshot{LastSuccessUtc=now,LastAttemptUtc=now,LastPath=Path.GetFullPath(target)}:before with {LastAttemptUtc=now,Error=failure.Message};
                File.WriteAllText(temp,JsonSerializer.Serialize(next));File.Move(temp,PathFor(database),true);
            }
            catch{ /* Backup data has priority: status-file failure must not invalidate a completed snapshot. */ }
            finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}}
        }
    }
}
