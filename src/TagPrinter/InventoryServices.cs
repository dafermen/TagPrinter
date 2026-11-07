using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
namespace TagPrinter;

/// <summary>A named design. Applying it preserves the current asset, language, printer and copy count.</summary>
/// <param name="Name">Unique user-visible name.</param>
/// <param name="Design">Formatting snapshot; identity fields are ignored when applied.</param>
public sealed record LabelProfile(string Name,WorkState Design)
{
    /// <summary>Copies formatting while preserving the editor's current asset identity and destination.</summary>
    public static WorkState Apply(WorkState current,WorkState design)=>design with {AssetType=current.AssetType,Brand=current.Brand,Model=current.Model,Serial=current.Serial,PartNumber=current.PartNumber,MacAddress=current.MacAddress,FreeText=current.FreeText,FreeTextMode=current.FreeTextMode,Printer=current.Printer,Copies=current.Copies,Language=current.Language,AssetUid=current.AssetUid,LabelPresetId="custom"};
    /// <summary>Removes asset values and printer identifiers before persisting a reusable design.</summary>
    public static WorkState Capture(WorkState state)=>state with {AssetType="",Brand="",Model="",Serial="",PartNumber="",MacAddress="",FreeText="",FreeTextMode=false,AssetUid="",Printer="",Copies=1,LabelPresetId="custom"};
}

/// <summary>Strict semicolon CSV import. Parsing and validation complete before any database write.</summary>
internal static class InventoryCsv
{
    internal const string Header="AssetType;Brand;Model;Serial;PartNumber;MacAddress";
    /// <summary>Reads quoted UTF-8/BOM fields, embedded newlines and leading-zero serials; rejects incomplete batches.</summary>
    internal static List<WorkState> Read(string path,WorkState design)
    {
        if(new FileInfo(path).Length>10*1024*1024)throw new InvalidDataException("CSV exceeds 10 MB.");
        using var parser=new TextFieldParser(path,new UTF8Encoding(false,true)){HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};parser.SetDelimiters(";");
        var header=parser.ReadFields()??throw new InvalidDataException("CSV header is missing.");
        if(header.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=header.Length || !header.Contains("Serial",StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("CSV requires a unique Serial header.");
        var result=new List<WorkState>();
        while(!parser.EndOfData)
        {
            var cells=parser.ReadFields()!;if(cells.Length!=header.Length)throw new InvalidDataException($"CSV record {result.Count+2}: column count mismatch.");
            string Value(string name,int max){int i=Array.FindIndex(header,h=>h.Equals(name,StringComparison.OrdinalIgnoreCase));string v=i<0?"":cells[i].Trim();if(v.Length>max || v.Contains('\0'))throw new InvalidDataException($"CSV record {result.Count+2}: invalid {name}.");return v;}
            var state=design with {FreeTextMode=false,FreeText="",AssetUid="",AssetType=Value("AssetType",80),Brand=Value("Brand",80),Model=Value("Model",100),Serial=Value("Serial",120),PartNumber=Value("PartNumber",100),MacAddress=Value("MacAddress",100)};
            if(string.IsNullOrWhiteSpace(state.Serial))throw new InvalidDataException($"CSV record {result.Count+2}: Serial is required.");
            result.Add(state);if(result.Count>10000)throw new InvalidDataException("Import supports at most 10000 records per file.");
        }
        if(result.Count==0)throw new InvalidDataException("CSV contains no inventory records.");return result;
    }
}

public sealed partial class HistoryStore
{
    /// <summary>Finds an active label serial or current canonical serial; masters use invariant Unicode normalization.</summary>
    public bool HasSerial(string serial)
    {
        using var c=Open();return HasSerial(c,null,serial);
    }
    /// <summary>Uses a bounded indexed lookup and the import transaction when present.</summary>
    private static bool HasSerial(SqliteConnection c,SqliteTransaction? transaction,string serial)
    {
        if(string.IsNullOrWhiteSpace(serial))return false;
        using var cmd=c.CreateCommand();cmd.Transaction=transaction;cmd.CommandText="SELECT 1 FROM (SELECT id FROM events WHERE trim(serial)=$s COLLATE NOCASE AND id NOT IN(SELECT event_id FROM trash) AND COALESCE(json_extract(state_json,'$.FreeTextMode'),0)=0 UNION ALL SELECT id FROM assets WHERE serial_key!='' AND serial_key=$key AND json_extract(record_json,'$.Status')!='Retired') LIMIT 1";cmd.Parameters.AddWithValue("$s",serial.Trim());cmd.Parameters.AddWithValue("$key",serial.Trim().ToUpperInvariant());return cmd.ExecuteScalar()!=null;
    }
    /// <summary>Atomically registers a validated batch as Imported actions, never as printed/exported labels.</summary>
    /// <returns>Inserted and skipped counts. Duplicate checks include earlier records in the same transaction.</returns>
    public (int Added,int Skipped) ImportInventory(IEnumerable<WorkState> rows,bool skipDuplicates=true)
    {
        var batch=rows.ToList();if(batch.Count is <1 or >10000 || batch.Any(s=>s.FreeTextMode || string.IsNullOrWhiteSpace(s.Serial) || s.Serial.Length>120))throw new InvalidDataException("Invalid inventory batch.");
        using var c=Open();using var transaction=c.BeginTransaction();int added=0,skipped=0;
        foreach(var state in batch)
        {
            if(skipDuplicates && HasSerial(c,transaction,state.Serial)){skipped++;continue;}
            // Imported rows resolve identity from their own serial, never from the currently selected label.
            Insert(c,transaction,"Imported",state with {Serial=state.Serial.Trim(),AssetUid=""});added++;
        }
        transaction.Commit();return (added,skipped);
    }
    /// <summary>Creates one automatic copy per UTC date, or a forced pre-operation copy; retains ten automatic files.</summary>
    public string? AutomaticBackup(string directory,bool force=false)
    {
        try
        {
        Directory.CreateDirectory(directory);
        string prefix="TagPrinter-auto-";var files=Directory.GetFiles(directory,prefix+"*.db").OrderByDescending(Path.GetFileName,StringComparer.Ordinal).ToList();
        if(!force && files.Any(f=>Path.GetFileName(f).StartsWith(prefix+DateTime.UtcNow.ToString("yyyyMMdd"),StringComparison.Ordinal)))return null;
        string path=Path.Combine(directory,prefix+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".db");
        Backup(path);
        foreach(string old in files.Skip(9))File.Delete(old);return path;
        }
        catch(Exception error){BackupStatus.Record(DatabasePath,directory,error);throw;}
    }
    /// <summary>Validates a read-only staged snapshot before replacing live pages through SQLite's transactional backup API.</summary>
    /// <returns>Safety backup path. The current work/settings and exported images are not restored.</returns>
    public string RestoreBackup(string sourcePath,string safetyDirectory)
    {
        if(string.Equals(Path.GetFullPath(sourcePath),DatabasePath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Select a separate backup.");
        string stage=Path.Combine(Path.GetDirectoryName(DatabasePath)!,"restore-stage-"+Guid.NewGuid()+".db");
        try
        {
            using(var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=sourcePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            using(var staged=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=stage,Pooling=false}.ToString())){source.Open();staged.Open();source.BackupDatabase(staged);}
            ValidateBackup(stage);
            using(var upgrade=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=stage,Pooling=false}.ToString())){upgrade.Open();AssetStore.Initialize(upgrade);ServiceStore.Initialize(upgrade);DatabasePerformance.Initialize(upgrade);}
            ValidateBackup(stage);
            Directory.CreateDirectory(safetyDirectory);string safety=Path.Combine(safetyDirectory,"TagPrinter-before-restore-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid()+".db");Backup(safety);
            using var candidate=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=stage,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());candidate.Open();using var destination=Open();candidate.BackupDatabase(destination);return safety;
        }
        finally{foreach(string file in new[]{stage,stage+"-wal",stage+"-shm"})if(File.Exists(file))File.Delete(file);}
    }
    /// <summary>Rejects corrupt, unrelated, newer, trigger-bearing or inconsistent snapshots without altering the live database.</summary>
    internal static void ValidateBackup(string path)
    {
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();
        using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA integrity_check";using(var reader=cmd.ExecuteReader()){while(reader.Read())if(reader.GetString(0)!="ok")throw new InvalidDataException("Backup integrity check failed.");}
        cmd.CommandText="PRAGMA user_version";int version=Convert.ToInt32(cmd.ExecuteScalar());if(version is not (1 or 2 or 3 or 4))throw new InvalidDataException("Unsupported backup schema version.");
        cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type IN ('trigger','view') OR (type='table' AND name NOT IN ('events','trash','sqlite_sequence','assets','asset_movements','asset_catalog','asset_services','service_changes'))";if(Convert.ToInt32(cmd.ExecuteScalar())!=0)throw new InvalidDataException("Unexpected backup schema objects.");
        cmd.CommandText="SELECT id,created_utc,action,state_json,output_path,source_event_id,serial,asset_type,brand,model,part_number,mac_address FROM events";
        var catalog=AppConfig.CreateDefault();
        using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            var e=Read(reader);if(!Actions.Contains(e.Action))throw new InvalidDataException("Unsupported action in backup.");
            ConfigurationTransfer.Validate(new ConfigurationBundle{Catalog=catalog,Work=e.State with {LabelPresetId="custom"}});
            string[] values=[e.State.Serial,e.State.AssetType,e.State.Brand,e.State.Model,e.State.PartNumber,e.State.MacAddress];
            for(int i=0;i<values.Length;i++)if(reader.GetString(6+i)!=values[i])throw new InvalidDataException("Backup metadata does not match its label snapshot.");
        }
        cmd.CommandText="SELECT event_id,deleted_utc FROM trash LIMIT 0";using(var reader=cmd.ExecuteReader()) { }
        foreach(var spec in new[]{("events","source_event_id"),("trash","event_id")})
        {
            cmd.CommandText="PRAGMA foreign_key_list("+spec.Item1+")";bool valid=false;
            using(var reader=cmd.ExecuteReader())while(reader.Read())if(reader.GetString(2)=="events" && reader.GetString(3)==spec.Item2 && reader.GetString(4)=="id")valid=true;
            if(!valid)throw new InvalidDataException("Missing backup foreign key constraints.");
        }
        if(version>=2)AssetStore.ValidateDatabase(c);
        if(version>=3){AssetCatalog.ValidateDatabase(c);cmd.CommandText="SELECT note FROM asset_movements";using var audit=cmd.ExecuteReader();while(audit.Read())if(audit.GetString(0).Length>1000 || audit.GetString(0).Contains('\0'))throw new InvalidDataException("Invalid movement note.");}
        if(version>=4)ServiceStore.ValidateDatabase(c);
        else {cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('asset_services','service_changes')";if(Convert.ToInt32(cmd.ExecuteScalar())!=0)throw new InvalidDataException("Service tables require schema 4.");}
        cmd.CommandText="PRAGMA foreign_key_check";using(var reader=cmd.ExecuteReader())if(reader.Read())throw new InvalidDataException("Broken backup source links.");
    }
}
