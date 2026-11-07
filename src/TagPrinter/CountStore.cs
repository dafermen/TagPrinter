using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

/// <summary>Physical inventory sessions share the local inventory database, with frozen scope and transactional reading audit.</summary>
internal sealed partial class CountStore
{
    private readonly HistoryStore history;
    private const string SessionColumns="id,revision,name,location,status,expected_count,created_utc,updated_utc,closed_utc,notes";
    private const string EntryColumns="id,session_id,asset_uid,expected,snapshot_json,lookup_key,observed,observed_utc,input_mode,input_value";
    /// <summary>Uses the same connection, backup and restore boundary as assets/services.</summary>
    internal CountStore(HistoryStore history){this.history=history;}
    /// <summary>Installs schema 5 atomically; it never derives physical observations from label-print history.</summary>
    internal static void Initialize(SqliteConnection c)
    {
        using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS count_sessions(id INTEGER PRIMARY KEY AUTOINCREMENT,revision INTEGER NOT NULL CHECK(revision>0),name TEXT NOT NULL,location TEXT NOT NULL,status TEXT NOT NULL CHECK(status IN ('Open','Closed','Canceled')),expected_count INTEGER NOT NULL CHECK(expected_count>=0),created_utc TEXT NOT NULL,updated_utc TEXT NOT NULL,closed_utc TEXT NOT NULL,notes TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_count_sessions ON count_sessions(status,id DESC);
            CREATE TABLE IF NOT EXISTS count_entries(id INTEGER PRIMARY KEY AUTOINCREMENT,session_id INTEGER NOT NULL REFERENCES count_sessions(id),asset_uid TEXT REFERENCES assets(uid),expected INTEGER NOT NULL CHECK(expected IN(0,1)),snapshot_json TEXT,lookup_key TEXT NOT NULL,observed INTEGER NOT NULL CHECK(observed IN(0,1)),observed_utc TEXT NOT NULL,input_mode TEXT NOT NULL,input_value TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_count_asset ON count_entries(session_id,asset_uid) WHERE asset_uid IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS ix_count_unknown ON count_entries(session_id,input_mode,lookup_key) WHERE asset_uid IS NULL;
            CREATE INDEX IF NOT EXISTS ix_count_frozen_serial ON count_entries(session_id,lookup_key) WHERE asset_uid IS NOT NULL AND lookup_key!='';
            CREATE INDEX IF NOT EXISTS ix_count_entry_page ON count_entries(session_id,id);
            CREATE INDEX IF NOT EXISTS ix_count_results ON count_entries(session_id,expected,observed,id);
            CREATE TABLE IF NOT EXISTS count_actions(id INTEGER PRIMARY KEY AUTOINCREMENT,session_id INTEGER NOT NULL REFERENCES count_sessions(id),revision INTEGER NOT NULL,at_utc TEXT NOT NULL,action TEXT NOT NULL CHECK(action IN ('Created','Seen','Duplicate','Undo','Closed','Canceled')),entry_id INTEGER REFERENCES count_entries(id),input_mode TEXT NOT NULL,input_value TEXT NOT NULL,result TEXT NOT NULL,resolution_json TEXT,note TEXT NOT NULL,UNIQUE(session_id,revision));
            CREATE INDEX IF NOT EXISTS ix_count_action_entry ON count_actions(entry_id,revision DESC);
            CREATE INDEX IF NOT EXISTS ix_count_effective ON count_actions(entry_id,revision DESC) WHERE action IN('Seen','Undo');
            CREATE INDEX IF NOT EXISTS ix_count_action_totals ON count_actions(session_id,action);
            PRAGMA user_version=5;
            """;cmd.ExecuteNonQuery();tx.Commit();
    }
    /// <summary>Reads one current session without opening the entire count history.</summary>
    internal CountSession? Get(long id){using var c=history.Open();return Get(c,null,id);}
    /// <summary>Reads within a caller-owned write transaction.</summary>
    private static CountSession? Get(SqliteConnection c,SqliteTransaction? tx,long id){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT "+SessionColumns+" FROM count_sessions WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);using var reader=cmd.ExecuteReader();return reader.Read()?Session(reader):null;}
    /// <summary>Maps normalized columns in the shared SessionColumns order.</summary>
    private static CountSession Session(SqliteDataReader r)=>new(r.GetInt64(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt64(5),r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9));
    /// <summary>Maps frozen JSON and observation metadata in the shared EntryColumns order.</summary>
    private static CountEntry Entry(SqliteDataReader r)=>new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetString(2),r.GetInt32(3)==1,r.IsDBNull(4)?null:JsonSerializer.Deserialize<AssetRecord>(r.GetString(4)),r.GetString(5),r.GetInt32(6)==1,r.GetString(7),r.GetString(8),r.GetString(9));
    /// <summary>Captures every non-retired asset at an exact canonical location in one transaction and SQL insert, with bounded managed memory.</summary>
    internal CountSession Create(string name,string location,string notes="")
    {
        name=name.Trim();location=location.Trim();notes=notes.Trim();Text(name,160,true);Text(location,160,true);Text(notes,2000);
        using var c=history.Open();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT name FROM asset_catalog WHERE kind='Location' AND value_key=$key";cmd.Parameters.AddWithValue("$key",AssetCatalog.Key(location));location=cmd.ExecuteScalar() as string??location;cmd.Parameters.Clear();string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        cmd.CommandText="INSERT INTO count_sessions(revision,name,location,status,expected_count,created_utc,updated_utc,closed_utc,notes) VALUES(1,$name,$location,'Open',0,$now,$now,'',$notes);SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$name",name);cmd.Parameters.AddWithValue("$location",location);cmd.Parameters.AddWithValue("$now",now);cmd.Parameters.AddWithValue("$notes",notes);long id=Convert.ToInt64(cmd.ExecuteScalar());cmd.Parameters.Clear();
        cmd.CommandText="INSERT INTO count_entries(session_id,asset_uid,expected,snapshot_json,lookup_key,observed,observed_utc,input_mode,input_value) SELECT $id,uid,1,record_json,serial_key,0,'','','' FROM assets WHERE json_extract(record_json,'$.Location')=$location AND json_extract(record_json,'$.Status')!='Retired'";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$location",location);long expected=cmd.ExecuteNonQuery();cmd.CommandText="UPDATE count_sessions SET expected_count=$expected WHERE id=$id";cmd.Parameters.AddWithValue("$expected",expected);cmd.ExecuteNonQuery();
        var session=new CountSession(id,1,name,location,"Open",expected,now,now,"",notes);Audit(c,tx,session,"Created",null,"","","",null,notes);tx.Commit();return session;
    }
    /// <summary>Rejects empty/control-bearing scanner payloads; explicit mode avoids confusing a serial with a TP asset code.</summary>
    internal static string Token(string mode,string value){if(mode is not ("Serial" or "AssetCode"))throw new InvalidDataException("Invalid reading mode.");value=value.Trim();Text(value,120,true);if(value.Any(char.IsControl))throw new InvalidDataException(Ui.T("Lee un solo identificador, sin saltos de línea.","Read one identifier without line breaks."));return value;}
    /// <summary>Checks bounded user strings before any write.</summary>
    private static void Text(string? value,int max,bool required=false){if(value is null || value.Length>max || value.Contains('\0') || (required && string.IsNullOrWhiteSpace(value)))throw new InvalidDataException(Ui.T("Revisa el nombre, ubicación o lectura.","Check the name, location or reading."));}
    /// <summary>Uses immutable expected serials plus current canonical lookup. Conflicting identities stay unresolved until an explicit asset-code reading.</summary>
    internal CountScan Scan(long sessionId,string mode,string input)
    {
        input=Token(mode,input);string key=AssetCatalog.Key(input);using var c=history.Open();using var tx=c.BeginTransaction();var session=RequireOpen(c,tx,sessionId);using var cmd=c.CreateCommand();cmd.Transaction=tx;
        AssetRecord? current=null;CountEntry? frozen=null;bool multiple=false;
        if(mode=="Serial")
        {
            cmd.CommandText="SELECT record_json FROM assets WHERE serial_key!='' AND serial_key=$key";cmd.Parameters.AddWithValue("$key",key);if(cmd.ExecuteScalar() is string json)current=JsonSerializer.Deserialize<AssetRecord>(json);
            cmd.CommandText="SELECT "+EntryColumns+" FROM count_entries WHERE session_id=$session AND asset_uid IS NOT NULL AND lookup_key!='' AND lookup_key=$key LIMIT 2";cmd.Parameters.AddWithValue("$session",sessionId);using var reader=cmd.ExecuteReader();if(reader.Read()){frozen=Entry(reader);multiple=reader.Read();}
        }
        else if(key.StartsWith("TP-",StringComparison.Ordinal) && long.TryParse(key.AsSpan(3),NumberStyles.None,CultureInfo.InvariantCulture,out long number) && number>0 && key=="TP-"+number.ToString("D8",CultureInfo.InvariantCulture))
        {
            cmd.CommandText="SELECT record_json FROM assets WHERE id=$number";cmd.Parameters.AddWithValue("$number",number);if(cmd.ExecuteScalar() is string json)current=JsonSerializer.Deserialize<AssetRecord>(json);
        }
        bool ambiguous=multiple || (frozen is not null && current is not null && frozen.AssetUid!=current.Uid);AssetRecord? resolution=ambiguous?null:current??frozen?.Snapshot;CountEntry? entry=ambiguous?null:frozen;
        cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$session",sessionId);
        if(entry is null)
        {
            cmd.CommandText="SELECT "+EntryColumns+" FROM count_entries WHERE session_id=$session AND "+(resolution is null?"asset_uid IS NULL AND input_mode=$mode AND lookup_key=$key":"asset_uid=$uid");cmd.Parameters.AddWithValue("$mode",mode);cmd.Parameters.AddWithValue("$key",key);cmd.Parameters.AddWithValue("$uid",resolution is null?DBNull.Value:resolution.Uid);using var reader=cmd.ExecuteReader();if(reader.Read())entry=Entry(reader);
        }
        if(entry is null)
        {
            cmd.Parameters.Clear();cmd.CommandText="INSERT INTO count_entries(session_id,asset_uid,expected,snapshot_json,lookup_key,observed,observed_utc,input_mode,input_value) VALUES($session,$uid,0,$json,$key,0,'',$mode,'');SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$session",sessionId);cmd.Parameters.AddWithValue("$uid",resolution is null?DBNull.Value:resolution.Uid);cmd.Parameters.AddWithValue("$json",resolution is null?DBNull.Value:JsonSerializer.Serialize(resolution));cmd.Parameters.AddWithValue("$key",resolution is null?key:AssetCatalog.Key(resolution.Serial));cmd.Parameters.AddWithValue("$mode",resolution is null?mode:"");long id=Convert.ToInt64(cmd.ExecuteScalar());entry=new(id,sessionId,resolution?.Uid,false,resolution,resolution is null?key:AssetCatalog.Key(resolution.Serial),false,"",resolution is null?mode:"","");
        }
        bool duplicate=entry.Observed;string result=resolution is null?(ambiguous?"Ambiguous":"Unknown"):Outcome(entry with {Observed=true},session);
        if(!duplicate)
        {
            cmd.Parameters.Clear();cmd.CommandText="UPDATE count_entries SET observed=1,observed_utc=$at,input_mode=$mode,input_value=$input WHERE id=$id";string at=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);cmd.Parameters.AddWithValue("$at",at);cmd.Parameters.AddWithValue("$mode",mode);cmd.Parameters.AddWithValue("$input",input);cmd.Parameters.AddWithValue("$id",entry.Id);cmd.ExecuteNonQuery();entry=entry with {Observed=true,ObservedUtc=at,InputMode=mode,InputValue=input};
        }
        session=Advance(c,tx,session,duplicate?"Duplicate":"Seen",entry.Id,mode,input,result,resolution);tx.Commit();return new(session,entry,result,duplicate);
    }
    /// <summary>Withdraws an effective reading without deleting evidence. An expected asset becomes pending again.</summary>
    internal CountSession Undo(long sessionId,long entryId,int revision)
    {
        using var c=history.Open();using var tx=c.BeginTransaction();var session=RequireOpen(c,tx,sessionId,revision);using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT "+EntryColumns+" FROM count_entries WHERE id=$id AND session_id=$session";cmd.Parameters.AddWithValue("$id",entryId);cmd.Parameters.AddWithValue("$session",sessionId);CountEntry entry;using(var reader=cmd.ExecuteReader()){if(!reader.Read())throw new InvalidDataException("Reading not found.");entry=Entry(reader);}if(!entry.Observed)throw new InvalidOperationException(Ui.T("El registro no tiene una lectura vigente.","The record has no effective reading."));
        cmd.CommandText="UPDATE count_entries SET observed=0,observed_utc='',input_value='',input_mode=CASE WHEN asset_uid IS NULL THEN input_mode ELSE '' END WHERE id=$id";cmd.ExecuteNonQuery();session=Advance(c,tx,session,"Undo",entryId,"","",Outcome(entry,session),null);tx.Commit();return session;
    }
    /// <summary>Closes or cancels an unchanged open session. Only Closed turns unobserved expected rows into Missing.</summary>
    internal CountSession Finish(long sessionId,int revision,bool cancel=false)
    {
        using var c=history.Open();using var tx=c.BeginTransaction();var session=RequireOpen(c,tx,sessionId,revision);session=Advance(c,tx,session,cancel?"Canceled":"Closed",null,"","","",null);tx.Commit();return session;
    }
    /// <summary>Rejects writes after closure and stale explicit Undo/Finish commands.</summary>
    private static CountSession RequireOpen(SqliteConnection c,SqliteTransaction tx,long id,int? revision=null){var s=Get(c,tx,id)??throw new InvalidDataException("Count session not found.");if(s.Status!="Open")throw new InvalidOperationException(Ui.T("La toma está cerrada o cancelada; sus resultados son de solo lectura.","The count is closed or canceled; its results are read-only."));if(revision is {} expected && expected!=s.Revision)throw new InvalidOperationException(Ui.T("La toma cambió. Actualiza antes de continuar.","The count changed. Refresh before continuing."));return s;}
    /// <summary>Updates the session revision and its append-only action in the same transaction as the observation.</summary>
    private static CountSession Advance(SqliteConnection c,SqliteTransaction tx,CountSession s,string action,long? entry,string mode,string value,string result,AssetRecord? resolution)
    {
        string now=action=="Seen"?GetObservedTime(c,tx,entry!.Value):DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);bool terminal=action is "Closed" or "Canceled";var next=s with {Revision=s.Revision+1,UpdatedUtc=now,Status=terminal?action:s.Status,ClosedUtc=terminal?now:s.ClosedUtc};using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE count_sessions SET revision=$revision,updated_utc=$at,status=$status,closed_utc=$closed WHERE id=$id AND revision=$previous";cmd.Parameters.AddWithValue("$revision",next.Revision);cmd.Parameters.AddWithValue("$at",now);cmd.Parameters.AddWithValue("$status",next.Status);cmd.Parameters.AddWithValue("$closed",next.ClosedUtc);cmd.Parameters.AddWithValue("$id",s.Id);cmd.Parameters.AddWithValue("$previous",s.Revision);if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Count changed during save.");Audit(c,tx,next,action,entry,mode,value,result,resolution,"");return next;
    }
    /// <summary>Uses the exact observation instant for Seen audit consistency.</summary>
    private static string GetObservedTime(SqliteConnection c,SqliteTransaction tx,long entry){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT observed_utc FROM count_entries WHERE id=$id";cmd.Parameters.AddWithValue("$id",entry);return (string)cmd.ExecuteScalar()!;}
    /// <summary>Appends one immutable audit action; a failed insert rolls back its associated business write.</summary>
    private static void Audit(SqliteConnection c,SqliteTransaction tx,CountSession s,string action,long? entry,string mode,string value,string result,AssetRecord? resolution,string note)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO count_actions(session_id,revision,at_utc,action,entry_id,input_mode,input_value,result,resolution_json,note) VALUES($session,$revision,$at,$action,$entry,$mode,$value,$result,$json,$note)";cmd.Parameters.AddWithValue("$session",s.Id);cmd.Parameters.AddWithValue("$revision",s.Revision);cmd.Parameters.AddWithValue("$at",s.UpdatedUtc);cmd.Parameters.AddWithValue("$action",action);cmd.Parameters.AddWithValue("$entry",entry is null?DBNull.Value:entry.Value);cmd.Parameters.AddWithValue("$mode",mode);cmd.Parameters.AddWithValue("$value",value);cmd.Parameters.AddWithValue("$result",result);cmd.Parameters.AddWithValue("$json",resolution is null?DBNull.Value:JsonSerializer.Serialize(resolution));cmd.Parameters.AddWithValue("$note",note);cmd.ExecuteNonQuery();
    }
    /// <summary>Classifies frozen evidence; no observation changes the administrative asset location or state.</summary>
    internal static string Outcome(CountEntry e,CountSession s)=>!e.Observed?(e.Expected?(s.Status=="Closed"?"Missing":"Pending"):"Withdrawn"):e.Expected?"Found":e.Snapshot is null?"Unknown":e.Snapshot.Status=="Retired"?"Retired":e.Snapshot.Location!=s.Location?"WrongLocation":"Unexpected";
    /// <summary>Translates stable statuses/results without changing stored codes or scanner input.</summary>
    internal static string Caption(string key)=>key switch{"Open"=>Ui.T("Abierta","Open"),"Closed"=>Ui.T("Cerrada","Closed"),"Canceled"=>Ui.T("Cancelada","Canceled"),"Created"=>Ui.T("Creada","Created"),"Seen"=>Ui.T("Lectura guardada","Reading saved"),"Duplicate"=>Ui.T("Lectura repetida","Duplicate reading"),"Undo"=>Ui.T("Lectura retirada","Reading withdrawn"),"Pending"=>Ui.T("Pendiente","Pending"),"Missing"=>Ui.T("Faltante","Missing"),"Found"=>Ui.T("Encontrado","Found"),"WrongLocation"=>Ui.T("Ubicación incorrecta","Wrong location"),"Unexpected"=>Ui.T("No esperado","Unexpected"),"Retired"=>Ui.T("Dado de baja","Retired"),"Unknown"=>Ui.T("No reconocido","Unrecognized"),"Ambiguous"=>Ui.T("Serial ambiguo; usa código del activo","Ambiguous serial; use asset code"),"Withdrawn"=>Ui.T("Retirado","Withdrawn"),"All"=>Ui.T("Todos los resultados","All results"),"AssetCode"=>Ui.T("Código del activo","Asset code"),_=>key};
}
