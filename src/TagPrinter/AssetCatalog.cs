using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

/// <summary>Reusable inventory vocabulary, separate from installation label presets and included in database backups.</summary>
internal sealed class AssetCatalog
{
    // Keys match AssetRecord properties; stable database values are never translated.
    internal static readonly string[] Kinds=["AssetType","Brand","Model","Location","Custodian"];
    private readonly HistoryStore history;
    /// <summary>Uses the inventory connection/transaction policy.</summary>
    internal AssetCatalog(HistoryStore history){this.history=history;}
    /// <summary>Normalizes only lookup keys; the first saved spelling remains the display value.</summary>
    internal static string Key(string value)=>value.Trim().ToUpperInvariant();
    /// <summary>Creates schema-3 catalogs/audit notes and learns existing values without changing prior asset snapshots.</summary>
    internal static void Initialize(SqliteConnection c,SqliteTransaction tx)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='asset_catalog'";bool seed=Convert.ToInt32(cmd.ExecuteScalar())==0;
        cmd.CommandText="CREATE TABLE IF NOT EXISTS asset_catalog(kind TEXT NOT NULL,value_key TEXT NOT NULL,name TEXT NOT NULL,PRIMARY KEY(kind,value_key))";cmd.ExecuteNonQuery();
        cmd.CommandText="PRAGMA table_info(asset_movements)";bool note=false;using(var reader=cmd.ExecuteReader())while(reader.Read())if(reader.GetString(1)=="note")note=true;
        if(!note){cmd.CommandText="ALTER TABLE asset_movements ADD COLUMN note TEXT NOT NULL DEFAULT ''";cmd.ExecuteNonQuery();}
        if(seed)
        {
            cmd.CommandText="SELECT record_json FROM assets ORDER BY id";
            using var reader=cmd.ExecuteReader();while(reader.Read())
            {
                var row=JsonSerializer.Deserialize<AssetRecord>(reader.GetString(0))??throw new InvalidDataException("Invalid catalog seed asset.");Normalize(c,tx,row);
            }
        }
    }
    /// <summary>Reads display names for selection/suggestions, preserving previously learned unused values.</summary>
    internal List<string> Values(string kind)
    {
        Validate(kind,"");using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT name FROM asset_catalog WHERE kind=$kind ORDER BY name COLLATE NOCASE";cmd.Parameters.AddWithValue("$kind",kind);using var reader=cmd.ExecuteReader();var result=new List<string>();while(reader.Read())result.Add(reader.GetString(0));return result;
    }
    /// <summary>Reads only a small indexed prefix result for dropdowns. Arbitrary substring scans are avoided.</summary>
    internal List<string> Suggest(string kind,string prefix="",int limit=200)
    {
        Validate(kind,"");if(limit is <1 or >250)throw new ArgumentOutOfRangeException(nameof(limit));using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT name FROM asset_catalog WHERE kind=$kind AND value_key LIKE $prefix ESCAPE '~' ORDER BY value_key COLLATE NOCASE LIMIT $limit";cmd.Parameters.AddWithValue("$kind",kind);cmd.Parameters.AddWithValue("$prefix",Key(prefix).Replace("~","~~").Replace("%","~%").Replace("_","~_")+"%");cmd.Parameters.AddWithValue("$limit",limit);using var reader=cmd.ExecuteReader();var rows=new List<string>();while(reader.Read())rows.Add(reader.GetString(0));return rows;
    }
    /// <summary>Adds one explicit catalog option atomically; case/edge-space duplicates reuse the original spelling.</summary>
    internal string Add(string kind,string value)
    {
        if(string.IsNullOrWhiteSpace(value))throw new InvalidDataException(Ui.T("Escribe un valor para el catálogo.","Enter a catalog value."));
        using var c=history.Open();using var tx=c.BeginTransaction();string saved=Resolve(c,tx,kind,value);tx.Commit();return saved;
    }
    /// <summary>Checks catalog kinds and field-specific limits before any insert.</summary>
    private static void Validate(string kind,string value)
    {
        int max=kind switch{"AssetType" or "Brand"=>80,"Model"=>100,"Location" or "Custodian"=>160,_=>throw new InvalidDataException("Unknown asset catalog.")};
        if(value.Length>max || value.Contains('\0'))throw new InvalidDataException(Ui.T("El valor del catálogo es demasiado largo o inválido.","Catalog value is too long or invalid."));
    }
    /// <summary>Resolves a spelling within the caller's transaction; new values commit only with the surrounding operation.</summary>
    private static string Resolve(SqliteConnection c,SqliteTransaction tx,string kind,string value)
    {
        value=value.Trim();Validate(kind,value);if(value.Length==0)return "";
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT OR IGNORE INTO asset_catalog(kind,value_key,name) VALUES($kind,$key,$name)";cmd.Parameters.AddWithValue("$kind",kind);cmd.Parameters.AddWithValue("$key",Key(value));cmd.Parameters.AddWithValue("$name",value);cmd.ExecuteNonQuery();cmd.CommandText="SELECT name FROM asset_catalog WHERE kind=$kind AND value_key=$key";return (string)cmd.ExecuteScalar()!;
    }
    /// <summary>Applies catalog spelling to editable masters; label-history JSON is never rewritten.</summary>
    internal static AssetRecord Normalize(SqliteConnection c,SqliteTransaction tx,AssetRecord row)=>row with
    {
        AssetType=Resolve(c,tx,"AssetType",row.AssetType),Brand=Resolve(c,tx,"Brand",row.Brand),Model=Resolve(c,tx,"Model",row.Model),Location=Resolve(c,tx,"Location",row.Location),Custodian=Resolve(c,tx,"Custodian",row.Custodian)
    };
    /// <summary>Creates a lazily resolved preview vocabulary, bounded by the imported rows rather than total inventory.</summary>
    internal PreviewNames OpenPreview()=>new(history);
    internal sealed class PreviewNames : IDisposable
    {
        private readonly SqliteConnection connection;
        private readonly SqliteTransaction transaction;
        private readonly SqliteCommand command,byUid,bySerial;
        private readonly Dictionary<(string,string),string> cache=[];
        internal PreviewNames(HistoryStore history)
        {
            connection=history.Open();transaction=connection.BeginTransaction(deferred:true);command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT name FROM asset_catalog WHERE kind=$kind AND value_key=$key";command.Parameters.AddWithValue("$kind","");command.Parameters.AddWithValue("$key","");command.Prepare();
            SqliteCommand Lookup(string column){var read=connection.CreateCommand();read.Transaction=transaction;read.CommandText="SELECT record_json FROM assets WHERE "+(column=="serial_key"?"serial_key!='' AND ":"")+column+"=$key";read.Parameters.AddWithValue("$key","");read.Prepare();return read;}
            byUid=Lookup("uid");bySerial=Lookup("serial_key");
        }
        internal AssetRecord? Get(string uid){byUid.Parameters["$key"].Value=Guid.TryParse(uid,out var id)?id.ToString("N"):uid;return byUid.ExecuteScalar() is string json?JsonSerializer.Deserialize<AssetRecord>(json):null;}
        internal AssetRecord? Find(string serial){if(string.IsNullOrWhiteSpace(serial))return null;bySerial.Parameters["$key"].Value=Key(serial);return bySerial.ExecuteScalar() is string json?JsonSerializer.Deserialize<AssetRecord>(json):null;}
        private string Name(string kind,string value)
        {
            value=value.Trim();if(value.Length==0)return "";var key=(kind,Key(value));if(cache.TryGetValue(key,out var known))return known;command.Parameters["$kind"].Value=kind;command.Parameters["$key"].Value=key.Item2;string name=command.ExecuteScalar() as string??value;cache.Add(key,name);return name;
        }
        internal AssetRecord Normalize(AssetRecord row)=>row with {AssetType=Name("AssetType",row.AssetType),Brand=Name("Brand",row.Brand),Model=Name("Model",row.Model),Location=Name("Location",row.Location),Custodian=Name("Custodian",row.Custodian)};
        public void Dispose(){byUid.Dispose();bySerial.Dispose();command.Dispose();transaction.Dispose();connection.Dispose();}
    }
    /// <summary>Captures vocabulary once for an entire import preview without modifying SQLite.</summary>
    internal Dictionary<(string Kind,string Key),string> Snapshot()
    {
        using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT kind,value_key,name FROM asset_catalog";using var reader=cmd.ExecuteReader();var names=new Dictionary<(string,string),string>();while(reader.Read())names.Add((reader.GetString(0),reader.GetString(1)),reader.GetString(2));return names;
    }
    /// <summary>Resolves existing/new-file spelling in an in-memory vocabulary; canceled previews cannot learn options.</summary>
    internal static AssetRecord Preview(AssetRecord row,Dictionary<(string Kind,string Key),string> names)
    {
        string Name(string kind,string value){value=value.Trim();if(value.Length==0)return "";var key=(kind,Key(value));if(!names.TryGetValue(key,out string? name)){name=value;names.Add(key,name);}return name;}
        return row with {AssetType=Name("AssetType",row.AssetType),Brand=Name("Brand",row.Brand),Model=Name("Model",row.Model),Location=Name("Location",row.Location),Custodian=Name("Custodian",row.Custodian)};
    }
    /// <summary>Rejects invalid catalog data in staged backups before replacing the user's inventory.</summary>
    internal static void ValidateDatabase(SqliteConnection c)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT kind,value_key,name FROM asset_catalog";using var reader=cmd.ExecuteReader();
        while(reader.Read()){string kind=reader.GetString(0),name=reader.GetString(2);Validate(kind,name);if(name.Length==0 || name!=name.Trim() || reader.GetString(1)!=Key(name))throw new InvalidDataException("Invalid asset catalog entry.");}
    }
}
