using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;
using System.Text.Json;
namespace TagPrinter;

/// <summary>
/// Combined history filters. UTC start is inclusive; UTC end is exclusive. Deleted selects recycled rather
/// than active events.
/// </summary>
/// <param name="Search">Literal partial search text; SQL wildcard characters are escaped.</param>
/// <param name="Field">Whitelisted field key, or all for six-field search.</param>
/// <param name="Action">Stable action code; empty skips action filtering.</param>
/// <param name="FromUtc">Inclusive UTC start; null removes the lower bound.</param>
/// <param name="ToUtcExclusive">Exclusive UTC end; null removes the upper bound.</param>
/// <param name="Deleted">True selects recycled events; false selects active events.</param>
public record HistoryQuery(string Search="",string Field="all",string Action="",DateTimeOffset? FromUtc=null,DateTimeOffset? ToUtcExclusive=null,bool Deleted=false);
/// <summary>
/// One completed output action, not one unique asset. State preserves its design and SourceEventId
/// identifies a reloaded source.
/// </summary>
/// <param name="Id">Stable numeric event ID assigned by SQLite.</param>
/// <param name="CreatedUtc">UTC event instant.</param>
/// <param name="Action">Completed operation code from HistoryStore.Actions.</param>
/// <param name="State">Full saved-work snapshot.</param>
/// <param name="OutputPath">Exported file path; empty for print jobs.</param>
/// <param name="SourceEventId">Optional source event of a reloaded label.</param>
public record HistoryEvent(long Id,DateTimeOffset CreatedUtc,string Action,WorkState State,string OutputPath,long? SourceEventId);
/// <summary>
/// Owns SQLite connections, event inserts, reversible deletion, parameterized searches, CSV and backups.
/// </summary>
public sealed class HistoryStore
{
    /// <summary>Absolute database path outside the portable installation.</summary>
    public string DatabasePath {get;}
    /// <summary>Stable operation codes; order also populates action selectors.</summary>
    public static readonly string[] Actions=["ExportTag","ExportCode","ExportZpl","PrintSubmitted"];
    /// <summary>
    /// Initializes compatible tables/indexes and rejects databases declaring a newer schema version.
    /// </summary>
    /// <param name="path">File path to read or write; test callers use their isolated verification directory.</param>
    public HistoryStore(string path)
    {
        DatabasePath=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection=Open();
        // Validate SQLite pages before schema initialization; never replace a damaged database automatically.
        using(var check=connection.CreateCommand())
        {
            check.CommandText="PRAGMA quick_check";
            using var reader=check.ExecuteReader();
            while(reader.Read())if(!string.Equals(reader.GetString(0),"ok",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SQLite integrity check failed: "+reader.GetString(0));
        }
        using(var version=connection.CreateCommand())
        {
            version.CommandText="PRAGMA user_version";
            if(Convert.ToInt32(version.ExecuteScalar())>1)throw new InvalidDataException("History database version is newer than this app.");
        }
        using var command=connection.CreateCommand();
        // Idempotent initialization keeps existing events. Metadata columns support queries; state_json
        // stores the full design. Separate trash membership preserves event IDs and provenance.
        command.CommandText="""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS events (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              created_utc TEXT NOT NULL,
              action TEXT NOT NULL,
              serial TEXT NOT NULL,
              asset_type TEXT NOT NULL,
              brand TEXT NOT NULL,
              model TEXT NOT NULL,
              part_number TEXT NOT NULL,
              mac_address TEXT NOT NULL,
              state_json TEXT NOT NULL,
              output_path TEXT NOT NULL,
              source_event_id INTEGER REFERENCES events(id)
            );
            CREATE INDEX IF NOT EXISTS ix_events_serial ON events(serial COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_events_created ON events(created_utc);
            CREATE TABLE IF NOT EXISTS trash(event_id INTEGER PRIMARY KEY REFERENCES events(id), deleted_utc TEXT NOT NULL);
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
    }
    /// <summary>Opens a caller-owned, unpooled connection with foreign keys and five-second busy timeout.</summary>
    /// <returns>Open caller-owned connection; dispose after use.</returns>
    SqliteConnection Open()
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=DatabasePath,Pooling=false}.ToString());connection.Open();
        using var command=connection.CreateCommand();command.CommandText="PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";command.ExecuteNonQuery();return connection;
    }
    /// <summary>
    /// Validates action/serial requirements, inserts metadata and full JSON design, and returns the event
    /// ID.
    /// </summary>
    /// <param name="action">Completed event code from the supported Actions list.</param>
    /// <param name="state">Complete saved-work snapshot.</param>
    /// <param name="outputPath">Exported file path; empty for printer submissions.</param>
    /// <param name="sourceEventId">Optional ID of the event loaded before this generation.</param>
    /// <param name="timestamp">Optional deterministic event instant; null uses current UTC.</param>
    /// <returns>Generated numeric event ID.</returns>
    /// <exception cref="ArgumentException">Action is unknown or a required code serial is empty.</exception>
    public long Add(string action,WorkState state,string outputPath="",long? sourceEventId=null,DateTimeOffset? timestamp=null)
    {
        if(!Actions.Contains(action))throw new ArgumentException("Unknown history action.");
        if(string.IsNullOrWhiteSpace(state.Serial) && (state.ShowCode || action=="ExportCode"))throw new ArgumentException("A generated code requires a serial.");
        using var connection=Open();using var command=connection.CreateCommand();
        command.CommandText="""
            INSERT INTO events(created_utc,action,serial,asset_type,brand,model,part_number,mac_address,state_json,output_path,source_event_id)
            VALUES($date,$action,$serial,$type,$brand,$model,$part,$mac,$state,$output,$source);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$date",(timestamp??DateTimeOffset.UtcNow).ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$action",action);command.Parameters.AddWithValue("$serial",state.Serial);
        command.Parameters.AddWithValue("$type",state.AssetType);command.Parameters.AddWithValue("$brand",state.Brand);command.Parameters.AddWithValue("$model",state.Model);
        command.Parameters.AddWithValue("$part",state.PartNumber);command.Parameters.AddWithValue("$mac",state.MacAddress);
        command.Parameters.AddWithValue("$state",JsonSerializer.Serialize(state));command.Parameters.AddWithValue("$output",outputPath);
        command.Parameters.AddWithValue("$source",(object?)sourceEventId??DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar());
    }
    /// <summary>
    /// Maps the six SELECT columns to an event and deserializes its saved design; column order is
    /// significant.
    /// </summary>
    /// <param name="reader">Reader positioned on a row with the documented SELECT column order.</param>
    /// <returns>Reconstructed event including design snapshot.</returns>
    static HistoryEvent Read(SqliteDataReader reader)=>new(reader.GetInt64(0),DateTimeOffset.Parse(reader.GetString(1),CultureInfo.InvariantCulture),reader.GetString(2),JsonSerializer.Deserialize<WorkState>(reader.GetString(3))??throw new InvalidDataException("Invalid saved label."),reader.GetString(4),reader.IsDBNull(5)?null:reader.GetInt64(5));
    /// <summary>Retrieves a stable ID, including recycled records; returns null if absent.</summary>
    /// <param name="id">Stable numeric event ID, independent of serial text and display order.</param>
    /// <returns>Matching event, or null if absent.</returns>
    public HistoryEvent? Get(long id)
    {
        using var connection=Open();using var command=connection.CreateCommand();command.CommandText="SELECT id,created_utc,action,state_json,output_path,source_event_id FROM events WHERE id=$id";command.Parameters.AddWithValue("$id",id);
        using var reader=command.ExecuteReader();return reader.Read()?Read(reader):null;
    }
    /// <summary>
    /// Combines active/trash scope with text, action and UTC filters, returning newest IDs first. User
    /// input uses SQL parameters.
    /// </summary>
    /// <param name="query">Combined filters; null selects all active events.</param>
    /// <returns>Matching events ordered by descending ID.</returns>
    public List<HistoryEvent> Query(HistoryQuery? query=null)
    {
        query??=new();using var connection=Open();using var command=connection.CreateCommand();
        // Always apply the active/recycled scope even when no other filters are selected.
        var where=new List<string>{query.Deleted?"id IN (SELECT event_id FROM trash)":"id NOT IN (SELECT event_id FROM trash)"};
        if(query.Search.Length>0)
        {
            // Whitelist SQL expressions rather than accepting arbitrary column names. Search input becomes a
            // parameter; escape LIKE wildcards for literal matching.
            string column=query.Field switch {"serial"=>"serial","asset_type"=>"asset_type","brand"=>"brand","model"=>"model","part_number"=>"part_number","mac_address"=>"mac_address",_=>"serial || char(10) || asset_type || char(10) || brand || char(10) || model || char(10) || part_number || char(10) || mac_address"};
            where.Add($"({column}) LIKE $search ESCAPE '\\' COLLATE NOCASE");
            command.Parameters.AddWithValue("$search","%"+query.Search.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%");
        }
        if(query.Action.Length>0){where.Add("action=$action");command.Parameters.AddWithValue("$action",query.Action);}
        if(query.FromUtc is { } from){where.Add("created_utc >= $from");command.Parameters.AddWithValue("$from",from.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));}
        if(query.ToUtcExclusive is { } to){where.Add("created_utc < $to");command.Parameters.AddWithValue("$to",to.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));}
        command.CommandText="SELECT id,created_utc,action,state_json,output_path,source_event_id FROM events"+(where.Count>0?" WHERE "+string.Join(" AND ",where):"")+" ORDER BY id DESC";
        using var reader=command.ExecuteReader();var rows=new List<HistoryEvent>();while(reader.Read())rows.Add(Read(reader));return rows;
    }
    /// <summary>
    /// Moves/restores selected IDs in one transaction; events remain unchanged and only trash membership
    /// changes.
    /// </summary>
    /// <param name="ids">Event IDs to recycle/restore; duplicates are processed once.</param>
    /// <param name="deleted">True recycles IDs; false restores them.</param>
    public void SetDeleted(IEnumerable<long> ids,bool deleted)
    {
        // Commit the whole selection together. Repeated deletion is harmless; restoration removes only trash markers.
        using var connection=Open();using var transaction=connection.BeginTransaction();
        foreach(long id in ids.Distinct())
        {
            using var command=connection.CreateCommand();command.Transaction=transaction;
            command.CommandText=deleted?"INSERT OR IGNORE INTO trash(event_id,deleted_utc) SELECT id,$date FROM events WHERE id=$id":"DELETE FROM trash WHERE event_id=$id";
            command.Parameters.AddWithValue("$id",id);if(deleted)command.Parameters.AddWithValue("$date",DateTimeOffset.UtcNow.ToString("O"));command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    /// <summary>
    /// Copies a consistent SQLite database including recycled events; rejects the active path as
    /// destination.
    /// </summary>
    /// <param name="target">Backup destination, different from the active database.</param>
    /// <exception cref="ArgumentException">Destination is the active database path.</exception>
    public void Backup(string target)
    {
        if(string.Equals(Path.GetFullPath(target),DatabasePath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a different backup file.");
        using var source=Open();using var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=target,Pooling=false}.ToString());destination.Open();source.BackupDatabase(destination);
    }
    /// <summary>
    /// Writes quoted semicolon-delimited UTF-8/BOM with invariant numbers, UTC times and complete JSON
    /// snapshots.
    /// </summary>
    /// <param name="path">File path to read or write; test callers use their isolated verification directory.</param>
    /// <param name="rows">Events in the intended CSV output order.</param>
    public static void ExportCsv(string path,IEnumerable<HistoryEvent> rows)
    {
        using var writer=new StreamWriter(path,false,new UTF8Encoding(true));
        // Doubles embedded quotes and surrounds a CSV cell with quotes, preserving delimiters and newlines.
        static string Quote(string text)=>"\""+text.Replace("\"","\"\"")+"\"";
        // Quotes each cell and joins them with semicolons into one CSV row.
        void Write(IEnumerable<string> fields)=>writer.WriteLine(string.Join(";",fields.Select(Quote)));
        // Stable English CSV headers are an import contract. Display translations must not change exported
        // field names.
        Write(["Id","CreatedUtc","Action","Serial","AssetType","Brand","Model","PartNumber","MacAddress","Printer","Copies","CodeType","WidthMm","HeightMm","OutputPath","SourceEventId","StateJson"]);
        foreach(var row in rows)
        {
            var state=row.State;
            Write([row.Id.ToString(CultureInfo.InvariantCulture),row.CreatedUtc.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture),row.Action,state.Serial,state.AssetType,state.Brand,state.Model,state.PartNumber,state.MacAddress,state.Printer,state.Copies.ToString(CultureInfo.InvariantCulture),!state.ShowCode && row.Action!="ExportCode"?"TextOnly":state.Code==1?"QR":"Code128",state.Width.ToString(CultureInfo.InvariantCulture),state.Height.ToString(CultureInfo.InvariantCulture),row.OutputPath,row.SourceEventId?.ToString(CultureInfo.InvariantCulture)??"",JsonSerializer.Serialize(state)]);
        }
    }
}
