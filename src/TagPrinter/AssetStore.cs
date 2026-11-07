using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

/// <summary>Canonical asset identity and administration data; printed labels are separate immutable events.</summary>
internal sealed record AssetRecord
{
    /// <summary>Stable GUID identity used by label history; serials may be edited.</summary>
    public string Uid {get;init;}="";
    /// <summary>Local numeric sequence used to display the immutable TP asset code.</summary>
    public long Number {get;init;}
    /// <summary>Optimistic concurrency version; stale edits are rejected.</summary>
    public int Revision {get;init;}
    /// <summary>Asset category, such as Monitor or Laptop.</summary>
    public string AssetType {get;init;}="";
    /// <summary>Manufacturer name.</summary>
    public string Brand {get;init;}="";
    /// <summary>Manufacturer model or model description.</summary>
    public string Model {get;init;}="";
    /// <summary>Optional serial; nonempty values are unique after normalization.</summary>
    public string Serial {get;init;}="";
    /// <summary>Optional part identifier.</summary>
    public string PartNumber {get;init;}="";
    /// <summary>Optional MAC text retained without automatic normalization.</summary>
    public string MacAddress {get;init;}="";
    /// <summary>Operational state key; captions are localized separately.</summary>
    public string Status {get;init;}="Available";
    /// <summary>Current site, area or physical location.</summary>
    public string Location {get;init;}="";
    /// <summary>Responsible person or team; required for Assigned assets.</summary>
    public string Custodian {get;init;}="";
    /// <summary>Multiline administrative notes.</summary>
    public string Notes {get;init;}="";
    /// <summary>UTC registration time, not a purchase or original manufacture date.</summary>
    public string CreatedUtc {get;init;}="";
    /// <summary>UTC timestamp of the latest administrative change.</summary>
    public string UpdatedUtc {get;init;}="";
    /// <summary>User-visible stable local code derived from Number.</summary>
    public string Code=>"TP-"+Number.ToString("D8",CultureInfo.InvariantCulture);
}
/// <summary>One immutable administrative change; snapshots retain prior assignments, location and state.</summary>
internal sealed record AssetMovement(long Id,string AtUtc,string Action,AssetRecord? Before,AssetRecord After,string Actor)
{
    /// <summary>Optional reason for this movement, separate from persistent asset notes.</summary>
    public string Note {get;init;}="";
}
/// <summary>Asset administration shares the inventory database and its consistent backup/restore policy.</summary>
internal sealed partial class AssetStore
{
    // Reuse one database path/connection policy so a backup covers history, masters and movements together.
    private readonly HistoryStore history;
    // Stable stored keys; translate only display captions, never the JSON/database values.
    internal static readonly string[] Statuses=["Available","Assigned","Repair","Retired","Unspecified"];
    /// <summary>Uses the same database and backup scope as label history.</summary>
    internal AssetStore(HistoryStore history){this.history=history;}
    /// <summary>Maps a stored lifecycle key to the current UI language.</summary>
    internal static string StatusCaption(string key)=>key switch{"Available"=>Ui.T("Disponible","Available"),"Assigned"=>Ui.T("Asignado","Assigned"),"Repair"=>Ui.T("En reparación","In repair"),"Retired"=>Ui.T("Dado de baja","Retired"),"Unspecified"=>Ui.T("Pendiente revisión","Needs review"),_=>key};
    /// <summary>Uses one culture-independent key for creation, imports, migration and duplicate checks.</summary>
    private static string SerialKey(string value)=>value.Trim().ToUpperInvariant();

    /// <summary>Creates schema 3 and derives canonical assets once from latest active nonempty serials in legacy history.</summary>
    internal static void Initialize(SqliteConnection c)
    {
        using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS assets(id INTEGER PRIMARY KEY AUTOINCREMENT,uid TEXT NOT NULL UNIQUE,serial_key TEXT NOT NULL,revision INTEGER NOT NULL,record_json TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_asset_serial ON assets(serial_key) WHERE serial_key!='';
            CREATE TABLE IF NOT EXISTS asset_movements(id INTEGER PRIMARY KEY AUTOINCREMENT,asset_uid TEXT NOT NULL REFERENCES assets(uid),at_utc TEXT NOT NULL,action TEXT NOT NULL,before_json TEXT,after_json TEXT NOT NULL,actor TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_asset_movements_uid ON asset_movements(asset_uid,id);
            """;cmd.ExecuteNonQuery();AssetCatalog.Initialize(c,tx);
        cmd.CommandText="PRAGMA table_info(events)";bool linked=false;using(var reader=cmd.ExecuteReader())while(reader.Read())if(reader.GetString(1)=="asset_uid")linked=true;
        if(!linked)
        {
            // Presence of this column is the migration boundary. Keep original snapshots untouched; add a separate link.
            cmd.CommandText="ALTER TABLE events ADD COLUMN asset_uid TEXT REFERENCES assets(uid)";cmd.ExecuteNonQuery();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            cmd.CommandText="SELECT serial,state_json FROM events WHERE id NOT IN(SELECT event_id FROM trash) AND trim(serial)!='' AND COALESCE(json_extract(state_json,'$.FreeTextMode'),0)=0 ORDER BY id DESC";
            using(var reader=cmd.ExecuteReader())while(reader.Read())
            {
                string key=SerialKey(reader.GetString(0));if(!seen.Add(key))continue;
                var state=JsonSerializer.Deserialize<WorkState>(reader.GetString(1))??throw new InvalidDataException("Invalid legacy label snapshot.");
                if(SerialKey(state.Serial)!=key)throw new InvalidDataException("Legacy serial metadata does not match its snapshot.");
                EnsureForLabel(c,tx,state,"Migrated");
            }
            // Use the same normalization as canonical uniqueness, including Unicode case, without retaining a custom function in the schema.
            c.CreateFunction<string,string>("tagprinter_serial_key",v=>SerialKey(v),isDeterministic:true);
            cmd.CommandText="UPDATE events SET asset_uid=(SELECT uid FROM assets WHERE serial_key=tagprinter_serial_key(events.serial)) WHERE trim(serial)!='' AND COALESCE(json_extract(state_json,'$.FreeTextMode'),0)=0";cmd.ExecuteNonQuery();
        }
        cmd.CommandText="PRAGMA user_version";if(Convert.ToInt32(cmd.ExecuteScalar())<3){cmd.CommandText="PRAGMA user_version=3";cmd.ExecuteNonQuery();}tx.Commit();
    }

    /// <summary>Registers a new serial once, without overwriting an existing asset from a reprint or historical snapshot.</summary>
    internal static string? EnsureForLabel(SqliteConnection c,SqliteTransaction? tx,WorkState state,string action="Registered")
    {
        if(state.FreeTextMode)return null;
        if(!string.IsNullOrWhiteSpace(state.AssetUid))
        {
            var selected=Get(c,tx,state.AssetUid);
            if(selected is not null)return selected.Uid; // Explicit stable identity survives serial edits and historical reprints.
        }
        if(string.IsNullOrWhiteSpace(state.Serial))return null;
        using(var find=c.CreateCommand())
        {
            find.Transaction=tx;find.CommandText="SELECT uid FROM assets WHERE serial_key!='' AND serial_key=$key";find.Parameters.AddWithValue("$key",SerialKey(state.Serial));if(find.ExecuteScalar() is string uid)return uid;
        }
        return Save(c,tx,new AssetRecord{Status="Unspecified",AssetType=state.AssetType,Brand=state.Brand,Model=state.Model,Serial=state.Serial,PartNumber=state.PartNumber,MacAddress=state.MacAddress},action).Uid;
    }
    /// <summary>Reads a fresh canonical snapshot by stable identity, including retired assets.</summary>
    internal AssetRecord? Get(string uid){using var c=history.Open();return Get(c,null,uid);}
    /// <summary>Reads through the caller's transaction; normalize GUID formats before matching the stored identity.</summary>
    private static AssetRecord? Get(SqliteConnection c,SqliteTransaction? tx,string uid)
    {
        uid=Guid.TryParse(uid,out var parsed)?parsed.ToString("N"):uid;
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT record_json FROM assets WHERE uid=$uid";cmd.Parameters.AddWithValue("$uid",uid);return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<AssetRecord>(json):null;
    }
    /// <summary>Validates and saves one asset plus its audit change atomically, rejecting stale concurrent edits.</summary>
    internal AssetRecord Save(AssetRecord record)
    {
        using var c=history.Open();using var tx=c.BeginTransaction();var saved=Save(c,tx,record,"Updated");tx.Commit();return saved;
    }
    /// <summary>Writes one revision and its before/after audit together inside the caller's transaction.</summary>
    private static AssetRecord Save(SqliteConnection c,SqliteTransaction? tx,AssetRecord record,string action,string note="")
    {
        Validate(record);var candidate=record with {AssetType=record.AssetType.Trim(),Brand=record.Brand.Trim(),Model=record.Model.Trim(),Serial=record.Serial.Trim(),PartNumber=record.PartNumber.Trim(),MacAddress=record.MacAddress.Trim(),Location=record.Location.Trim(),Custodian=record.Custodian.Trim(),Notes=record.Notes.Trim()};Validate(candidate);
        var before=candidate.Uid.Length==0?null:Get(c,tx,candidate.Uid);
        if(candidate.Uid.Length>0 && before is null)throw new InvalidOperationException(Ui.T("El activo ya no existe. Actualiza la lista.","Asset no longer exists. Refresh the list."));
        if(before is not null && before.Revision!=candidate.Revision)throw new InvalidOperationException(Ui.T("El activo cambió en otra ventana. Recárgalo antes de guardar.","Asset changed elsewhere. Reload it before saving."));
        if(tx is null)throw new InvalidOperationException("Asset writes require a transaction.");
        candidate=AssetCatalog.Normalize(c,tx,candidate);
        if(note.Length>1000 || note.Contains('\0'))throw new InvalidDataException("Invalid movement note.");
        if(before is not null && string.IsNullOrWhiteSpace(note) && (before with {UpdatedUtc=""})==(candidate with {UpdatedUtc=""}))return before;
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        candidate=candidate with {Number=before?.Number??0,Uid=before?.Uid??Guid.NewGuid().ToString("N"),Revision=(before?.Revision??0)+1,CreatedUtc=before?.CreatedUtc??now,UpdatedUtc=now};
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        if(before is null)
        {
            cmd.CommandText="INSERT INTO assets(uid,serial_key,revision,record_json) VALUES($uid,$key,$revision,$json);SELECT last_insert_rowid();";
        }
        else cmd.CommandText="UPDATE assets SET serial_key=$key,revision=$revision,record_json=$json WHERE uid=$uid AND revision=$previous;SELECT changes();";
        cmd.Parameters.AddWithValue("$uid",candidate.Uid);cmd.Parameters.AddWithValue("$key",SerialKey(candidate.Serial));cmd.Parameters.AddWithValue("$revision",candidate.Revision);cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(candidate));cmd.Parameters.AddWithValue("$previous",before?.Revision??0);
        long result;
        try{result=Convert.ToInt64(cmd.ExecuteScalar());}catch(SqliteException e)when(e.SqliteExtendedErrorCode==2067){throw new InvalidOperationException(Ui.T("El serial ya pertenece a otro activo.","Serial already belongs to another asset."),e);}
        if(before is not null && result!=1)throw new InvalidOperationException("Asset changed during save.");
        // SQLite assigns the local sequence; persist it in the snapshot before adding the same snapshot to audit.
        if(before is null){candidate=candidate with {Number=result};cmd.CommandText="UPDATE assets SET record_json=$json WHERE uid=$uid";cmd.Parameters["$json"].Value=JsonSerializer.Serialize(candidate);cmd.ExecuteNonQuery();}
        cmd.Parameters.Clear();cmd.CommandText="INSERT INTO asset_movements(asset_uid,at_utc,action,before_json,after_json,actor,note) VALUES($uid,$at,$action,$before,$after,$actor,$note)";
        cmd.Parameters.AddWithValue("$uid",candidate.Uid);cmd.Parameters.AddWithValue("$at",now);cmd.Parameters.AddWithValue("$action",before is null?(action=="Updated"?"Created":action):action);cmd.Parameters.AddWithValue("$before",before is null?DBNull.Value:JsonSerializer.Serialize(before));cmd.Parameters.AddWithValue("$after",JsonSerializer.Serialize(candidate));cmd.Parameters.AddWithValue("$actor",Environment.UserName);cmd.Parameters.AddWithValue("$note",note.Trim());cmd.ExecuteNonQuery();return candidate;
    }
    /// <summary>Checks asset content independently of printer/layout preferences.</summary>
    internal static void Validate(AssetRecord r)
    {
        void Text(string? value,int max){if(value is null || value.Length>max || value.Contains('\0'))throw new InvalidDataException("Invalid asset field.");}
        Text(r.Uid,64);if(r.Uid.Length>0 && !Guid.TryParse(r.Uid,out _))throw new InvalidDataException("Invalid asset identity.");
        Text(r.AssetType,80);Text(r.Brand,80);Text(r.Model,100);Text(r.Serial,120);Text(r.PartNumber,100);Text(r.MacAddress,100);Text(r.Location,160);Text(r.Custodian,160);Text(r.Notes,2000);
        if(!Statuses.Contains(r.Status) || string.IsNullOrWhiteSpace(r.AssetType+r.Model+r.Serial))throw new InvalidDataException(Ui.T("Indica tipo, modelo o serial y un estado válido.","Enter an asset type, model or serial and a valid status."));
        if(r.Status=="Assigned" && string.IsNullOrWhiteSpace(r.Custodian))throw new InvalidDataException(Ui.T("Indica el responsable del activo asignado.","Enter the custodian for an assigned asset."));
    }
    /// <summary>Builds literal search/status predicates with bound values.</summary>
    private static string Where(SqliteCommand cmd,string search,string status,string? groupType=null,string? groupBrand=null,string? groupModel=null,AssetFilter? scope=null)
    {
        string where="1=1";
        if(groupType is not null){where+=" AND trim(json_extract(record_json,'$.AssetType'))=$gt AND trim(json_extract(record_json,'$.Brand'))=$gb AND trim(json_extract(record_json,'$.Model'))=$gm";cmd.Parameters.AddWithValue("$gt",groupType);cmd.Parameters.AddWithValue("$gb",groupBrand??"");cmd.Parameters.AddWithValue("$gm",groupModel??"");}
        if(status.Length>0){where+=" AND json_extract(record_json,'$.Status')=$status";cmd.Parameters.AddWithValue("$status",status);}
        if(scope?.ExactSerial==true && search.Length>0){where+=" AND serial_key!='' AND serial_key=$exact";cmd.Parameters.AddWithValue("$exact",SerialKey(search));}
        else if(search.Length>0)
        {
            string[] fields=["AssetType","Brand","Model","Serial","PartNumber","MacAddress","Location","Custodian","Notes"];
            string values=string.Join("||char(10)||",fields.Select(n=>$"COALESCE(json_extract(record_json,'$.{n}'),'')"));
            where+=" AND (printf('TP-%08d',id)||char(10)||"+values+") LIKE $search ESCAPE '~'";
            cmd.Parameters.AddWithValue("$search","%"+search.Replace("~","~~").Replace("%","~%").Replace("_","~_")+"%");
        }
        if(scope is not null)
        {
            if(scope.AssetUid is not null){where+=" AND uid=$assetUid";cmd.Parameters.AddWithValue("$assetUid",scope.AssetUid);}
            if(scope.ExcludeRetired)where+=" AND json_extract(record_json,'$.Status')!='Retired'";
            foreach(var pair in new[]{("Location",scope.Location),("Custodian",scope.Custodian)})if(pair.Item2 is not null){where+=" AND json_extract(record_json,'$."+pair.Item1+"')=$"+pair.Item1;cmd.Parameters.AddWithValue("$"+pair.Item1,pair.Item2);}
            if(scope.FromUtc is {} start){where+=" AND json_extract(record_json,'$.UpdatedUtc')>=$from";cmd.Parameters.AddWithValue("$from",start.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));}
            if(scope.ToUtc is {} end){where+=" AND json_extract(record_json,'$.UpdatedUtc')<$to";cmd.Parameters.AddWithValue("$to",end.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));}
        }
        return where;
    }
    /// <summary>Counts matching master records without loading their JSON into the UI.</summary>
    internal long Count(string search="",string status="",string? groupType=null,string? groupBrand=null,string? groupModel=null,AssetFilter? scope=null)
    {
        using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM assets WHERE "+Where(cmd,search,status,groupType,groupBrand,groupModel,scope);return Convert.ToInt64(cmd.ExecuteScalar());
    }
    /// <summary>Reads a bounded page with whitelisted global sort and literal search/group values.</summary>
    internal List<AssetRecord> Query(string search="",string status="",int offset=0,int limit=50,string? groupType=null,string? groupBrand=null,string? groupModel=null,string sort="Updated",bool descending=true,AssetFilter? scope=null)
    {
        if(offset<0 || limit is <1 or >250)throw new ArgumentOutOfRangeException(nameof(limit));
        string column=SortColumn(sort);
        using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT record_json FROM assets WHERE "+Where(cmd,search,status,groupType,groupBrand,groupModel,scope)+" ORDER BY "+column+(descending?" DESC":" ASC")+",id DESC LIMIT $limit OFFSET $offset";cmd.Parameters.AddWithValue("$limit",limit);cmd.Parameters.AddWithValue("$offset",offset);using var reader=cmd.ExecuteReader();var rows=new List<AssetRecord>();while(reader.Read())rows.Add(JsonSerializer.Deserialize<AssetRecord>(reader.GetString(0))!);return rows;
    }
    /// <summary>Whitelists SQL ordering for page reads; default ordering matches the update index.</summary>
    internal static string SortColumn(string sort)=>sort=="Code"?"id":new[]{"AssetType","Brand","Model","Serial","Status","Location","Custodian","PartNumber","MacAddress"}.Contains(sort)?$"json_extract(record_json,'$.{sort}') COLLATE NOCASE":"json_extract(record_json,'$.UpdatedUtc')";
    /// <summary>Reads append-only administrative changes for one asset, newest first.</summary>
    internal List<AssetMovement> Movements(string uid)
    {
        using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT id,at_utc,action,before_json,after_json,actor,note FROM asset_movements WHERE asset_uid=$uid ORDER BY id DESC";cmd.Parameters.AddWithValue("$uid",uid);using var reader=cmd.ExecuteReader();var result=new List<AssetMovement>();while(reader.Read())result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:JsonSerializer.Deserialize<AssetRecord>(reader.GetString(3)),JsonSerializer.Deserialize<AssetRecord>(reader.GetString(4))!,reader.GetString(5)){Note=reader.GetString(6)});return result;
    }
    /// <summary>Aggregates canonical records in SQL, keeping Dashboard memory bounded by groups rather than label-event history.</summary>
    internal (List<(string Type,string Brand,string Model,long Count)> Groups,Dictionary<string,long> States) Dashboard(string search,DateTimeOffset? from,DateTimeOffset? to,bool includeRetired)
    {
        using var c=history.Open();using var cmd=c.CreateCommand();string where=Where(cmd,search,"");
        if(!includeRetired)where+=" AND json_extract(record_json,'$.Status')!='Retired'";
        if(from is {} start){where+=" AND json_extract(record_json,'$.UpdatedUtc')>=$from";cmd.Parameters.AddWithValue("$from",start.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));}
        if(to is {} end){where+=" AND json_extract(record_json,'$.UpdatedUtc')<$to";cmd.Parameters.AddWithValue("$to",end.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));}
        cmd.CommandText="SELECT json_extract(record_json,'$.AssetType'),json_extract(record_json,'$.Brand'),json_extract(record_json,'$.Model'),COUNT(*) FROM assets WHERE "+where+" GROUP BY 1,2,3 ORDER BY 4 DESC,1,2,3";
        var groups=new List<(string,string,string,long)>();using(var reader=cmd.ExecuteReader())while(reader.Read())groups.Add((reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetInt64(3)));
        cmd.CommandText="SELECT json_extract(record_json,'$.Status'),COUNT(*) FROM assets WHERE "+where+" GROUP BY 1";var states=new Dictionary<string,long>();using(var reader=cmd.ExecuteReader())while(reader.Read())states[reader.GetString(0)]=reader.GetInt64(1);return (groups,states);
    }
    /// <summary>Checks schema-2 master/audit payloads and links before a restore can replace live inventory.</summary>
    internal static void ValidateDatabase(SqliteConnection c)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT id,uid,serial_key,revision,record_json FROM assets";
        using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            var row=JsonSerializer.Deserialize<AssetRecord>(reader.GetString(4))??throw new InvalidDataException("Missing asset snapshot.");Validate(row);
            if(row.Number!=reader.GetInt64(0) || row.Uid!=reader.GetString(1) || SerialKey(row.Serial)!=reader.GetString(2) || row.Revision!=reader.GetInt32(3) || row.Revision<1 || !DateTimeOffset.TryParse(row.CreatedUtc,CultureInfo.InvariantCulture,out _) || !DateTimeOffset.TryParse(row.UpdatedUtc,CultureInfo.InvariantCulture,out _))throw new InvalidDataException("Invalid asset metadata.");
        }
        cmd.CommandText="SELECT asset_uid,at_utc,before_json,after_json,action,actor FROM asset_movements";
        using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            string uid=reader.GetString(0);if(!DateTimeOffset.TryParse(reader.GetString(1),CultureInfo.InvariantCulture,out _))throw new InvalidDataException("Invalid asset movement timestamp.");
            foreach(int column in new[]{2,3})if(!reader.IsDBNull(column)){var row=JsonSerializer.Deserialize<AssetRecord>(reader.GetString(column))??throw new InvalidDataException("Missing movement data.");Validate(row);if(row.Uid!=uid)throw new InvalidDataException("Mismatched asset audit identity.");}
            if(!new[]{"Created","Updated","Registered","Migrated","Imported","ImportUpdated","Assigned","Returned","RepairStarted"}.Contains(reader.GetString(4)))throw new InvalidDataException("Unsupported asset movement.");
        }
        cmd.CommandText="SELECT asset_uid FROM events LIMIT 0";using(var reader=cmd.ExecuteReader()) { }
        foreach(var spec in new[]{("events","asset_uid"),("asset_movements","asset_uid")})
        {
            cmd.CommandText="PRAGMA foreign_key_list("+spec.Item1+")";bool valid=false;
            using(var reader=cmd.ExecuteReader())while(reader.Read())if(reader.GetString(2)=="assets" && reader.GetString(3)==spec.Item2 && reader.GetString(4)=="uid")valid=true;
            if(!valid)throw new InvalidDataException("Missing asset foreign key.");
        }
    }

}
