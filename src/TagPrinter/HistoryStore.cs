using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;
using System.Text.Json;
namespace TagPrinter;

public record HistoryQuery(string Search="",string Field="all",string Action="",DateTimeOffset? FromUtc=null,DateTimeOffset? ToUtcExclusive=null,bool Deleted=false);
public record HistoryEvent(long Id,DateTimeOffset CreatedUtc,string Action,WorkState State,string OutputPath,long? SourceEventId);
public sealed class HistoryStore
{
    public string DatabasePath {get;}
    public static readonly string[] Actions=["ExportTag","ExportCode","ExportZpl","PrintSubmitted"];
    public HistoryStore(string path)
    {
        DatabasePath=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection=Open();
        using(var version=connection.CreateCommand())
        {
            version.CommandText="PRAGMA user_version";
            if(Convert.ToInt32(version.ExecuteScalar())>1)throw new InvalidDataException("History database version is newer than this app.");
        }
        using var command=connection.CreateCommand();
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
    SqliteConnection Open()
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=DatabasePath,Pooling=false}.ToString());connection.Open();
        using var command=connection.CreateCommand();command.CommandText="PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";command.ExecuteNonQuery();return connection;
    }
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
    static HistoryEvent Read(SqliteDataReader reader)=>new(reader.GetInt64(0),DateTimeOffset.Parse(reader.GetString(1),CultureInfo.InvariantCulture),reader.GetString(2),JsonSerializer.Deserialize<WorkState>(reader.GetString(3))??throw new InvalidDataException("Invalid saved label."),reader.GetString(4),reader.IsDBNull(5)?null:reader.GetInt64(5));
    public HistoryEvent? Get(long id)
    {
        using var connection=Open();using var command=connection.CreateCommand();command.CommandText="SELECT id,created_utc,action,state_json,output_path,source_event_id FROM events WHERE id=$id";command.Parameters.AddWithValue("$id",id);
        using var reader=command.ExecuteReader();return reader.Read()?Read(reader):null;
    }
    public List<HistoryEvent> Query(HistoryQuery? query=null)
    {
        query??=new();using var connection=Open();using var command=connection.CreateCommand();
        var where=new List<string>{query.Deleted?"id IN (SELECT event_id FROM trash)":"id NOT IN (SELECT event_id FROM trash)"};
        if(query.Search.Length>0)
        {
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
    public void SetDeleted(IEnumerable<long> ids,bool deleted)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();
        foreach(long id in ids.Distinct())
        {
            using var command=connection.CreateCommand();command.Transaction=transaction;
            command.CommandText=deleted?"INSERT OR IGNORE INTO trash(event_id,deleted_utc) SELECT id,$date FROM events WHERE id=$id":"DELETE FROM trash WHERE event_id=$id";
            command.Parameters.AddWithValue("$id",id);if(deleted)command.Parameters.AddWithValue("$date",DateTimeOffset.UtcNow.ToString("O"));command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    public void Backup(string target)
    {
        if(string.Equals(Path.GetFullPath(target),DatabasePath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a different backup file.");
        using var source=Open();using var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=target,Pooling=false}.ToString());destination.Open();source.BackupDatabase(destination);
    }
    public static void ExportCsv(string path,IEnumerable<HistoryEvent> rows)
    {
        using var writer=new StreamWriter(path,false,new UTF8Encoding(true));
        static string Quote(string text)=>"\""+text.Replace("\"","\"\"")+"\"";
        void Write(IEnumerable<string> fields)=>writer.WriteLine(string.Join(";",fields.Select(Quote)));
        Write(["Id","CreatedUtc","Action","Serial","AssetType","Brand","Model","PartNumber","MacAddress","Printer","Copies","CodeType","WidthMm","HeightMm","OutputPath","SourceEventId","StateJson"]);
        foreach(var row in rows)
        {
            var state=row.State;
            Write([row.Id.ToString(CultureInfo.InvariantCulture),row.CreatedUtc.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture),row.Action,state.Serial,state.AssetType,state.Brand,state.Model,state.PartNumber,state.MacAddress,state.Printer,state.Copies.ToString(CultureInfo.InvariantCulture),!state.ShowCode && row.Action!="ExportCode"?"TextOnly":state.Code==1?"QR":"Code128",state.Width.ToString(CultureInfo.InvariantCulture),state.Height.ToString(CultureInfo.InvariantCulture),row.OutputPath,row.SourceEventId?.ToString(CultureInfo.InvariantCulture)??"",JsonSerializer.Serialize(state)]);
        }
    }
}
