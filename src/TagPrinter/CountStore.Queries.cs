using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

internal sealed partial class CountStore
{
    // Keep this expression aligned with Outcome(). It reads frozen evidence, never the current master location.
    private const string ResultSql="CASE WHEN e.observed=0 THEN CASE WHEN e.expected=1 THEN CASE WHEN s.status='Closed' THEN 'Missing' ELSE 'Pending' END ELSE 'Withdrawn' END WHEN e.expected=1 THEN 'Found' WHEN e.asset_uid IS NULL THEN 'Unknown' WHEN json_extract(e.snapshot_json,'$.Status')='Retired' THEN 'Retired' WHEN json_extract(e.snapshot_json,'$.Location')!=s.location THEN 'WrongLocation' ELSE 'Unexpected' END";
    private const string Joined=" FROM count_entries e JOIN count_sessions s ON s.id=e.session_id ";
    private static readonly string SelectedEntry=string.Join(',',EntryColumns.Split(',').Select(c=>"e."+c));
    /// <summary>Reads a bounded searchable session page; status values are internal keys rather than translated captions.</summary>
    internal DatabasePage<CountSession> Sessions(string search="",string status="",int page=0,int size=25,CancellationToken token=default)
    {
        if(page<0 || size is <1 or >100 || (status!="" && status is not ("Open" or "Closed" or "Canceled")))throw new ArgumentException("Invalid count session filter.");
        return DatabaseRead.Run(history,token,scope=>
        {
            using var cmd=scope.Command();string where="1=1";if(status.Length>0){where+=" AND status=$status";cmd.Parameters.AddWithValue("$status",status);}if(search.Length>0){where+=" AND (printf('PC-%08d',id)||char(10)||name||char(10)||location||char(10)||notes) LIKE $search ESCAPE '~'";cmd.Parameters.AddWithValue("$search",Like(search));}cmd.CommandText="SELECT COUNT(*) FROM count_sessions WHERE "+where;long total=Convert.ToInt64(cmd.ExecuteScalar());int clamped=(int)Math.Min(page,Math.Max(0,(total-1)/size));cmd.CommandText="SELECT "+SessionColumns+" FROM count_sessions WHERE "+where+" ORDER BY id DESC LIMIT $limit OFFSET $offset";cmd.Parameters.AddWithValue("$limit",size);cmd.Parameters.AddWithValue("$offset",clamped*size);using var reader=cmd.ExecuteReader();var rows=new List<CountSession>();while(reader.Read()){token.ThrowIfCancellationRequested();rows.Add(Session(reader));}return new DatabasePage<CountSession>(total,clamped,rows);
        });
    }
    /// <summary>Escapes literal percent/underscore/escape characters before parameterized LIKE matching.</summary>
    private static string Like(string value)=>"%"+value.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_")+"%";
    /// <summary>Builds only a whitelisted outcome filter and bound literal search.</summary>
    private static string ResultsWhere(SqliteCommand cmd,long sessionId,string result,string search)
    {
        if(!new[]{"All","Pending","Missing","Found","WrongLocation","Unexpected","Retired","Unknown","Withdrawn"}.Contains(result))throw new ArgumentException("Invalid count outcome filter.");
        cmd.Parameters.AddWithValue("$session",sessionId);string where="e.session_id=$session";
        if(result=="All")where+=" AND (e.expected=1 OR e.observed=1)";else{where+=" AND ("+ResultSql+")=$result";cmd.Parameters.AddWithValue("$result",result);}
        if(search.Length>0){where+=" AND (COALESCE(json_extract(e.snapshot_json,'$.Serial'),'')||char(10)||COALESCE(printf('TP-%08d',json_extract(e.snapshot_json,'$.Number')),'')||char(10)||COALESCE(json_extract(e.snapshot_json,'$.AssetType'),'')||char(10)||COALESCE(json_extract(e.snapshot_json,'$.Brand'),'')||char(10)||COALESCE(json_extract(e.snapshot_json,'$.Model'),'')||char(10)||e.input_value||char(10)||e.lookup_key) LIKE $search ESCAPE '~'";cmd.Parameters.AddWithValue("$search",Like(search));}return where;
    }
    /// <summary>Reads totals and one 50-row result page from one cancellable snapshot.</summary>
    internal CountPage Results(long sessionId,string result="All",string search="",int page=0,int size=50,CancellationToken token=default)
    {
        if(page<0 || size is <1 or >250)throw new ArgumentOutOfRangeException(nameof(size));return DatabaseRead.Run(history,token,scope=>
        {
            using var cmd=scope.Command();cmd.CommandText="SELECT "+SessionColumns+" FROM count_sessions WHERE id=$id";cmd.Parameters.AddWithValue("$id",sessionId);CountSession session;using(var reader=cmd.ExecuteReader()){if(!reader.Read())throw new InvalidDataException("Count session not found.");session=Session(reader);}cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$session",sessionId);
            cmd.CommandText="SELECT COALESCE(SUM(e.expected=1 AND e.observed=1),0),COALESCE(SUM(e.expected=1 AND e.observed=0),0),COALESCE(SUM(("+ResultSql+")='WrongLocation'),0),COALESCE(SUM(("+ResultSql+")='Unexpected'),0),COALESCE(SUM(("+ResultSql+")='Retired'),0),COALESCE(SUM(("+ResultSql+")='Unknown'),0)"+Joined+"WHERE e.session_id=$session";long found,pending,wrong,extra,retired,unknown;using(var reader=cmd.ExecuteReader()){reader.Read();found=reader.GetInt64(0);pending=reader.GetInt64(1);wrong=reader.GetInt64(2);extra=reader.GetInt64(3);retired=reader.GetInt64(4);unknown=reader.GetInt64(5);}cmd.CommandText="SELECT COALESCE(SUM(action IN('Seen','Duplicate')),0),COALESCE(SUM(action='Duplicate'),0) FROM count_actions WHERE session_id=$session";long readings,duplicates;using(var reader=cmd.ExecuteReader()){reader.Read();readings=reader.GetInt64(0);duplicates=reader.GetInt64(1);}cmd.Parameters.Clear();string where=ResultsWhere(cmd,sessionId,result,search);cmd.CommandText="SELECT COUNT(*)"+Joined+"WHERE "+where;long total=Convert.ToInt64(cmd.ExecuteScalar());int clamped=(int)Math.Min(page,Math.Max(0,(total-1)/size));
            cmd.CommandText="SELECT "+SelectedEntry+","+ResultSql+Joined+"WHERE "+where+" ORDER BY e.id LIMIT $limit OFFSET $offset";cmd.Parameters.AddWithValue("$limit",size);cmd.Parameters.AddWithValue("$offset",clamped*size);using var rows=cmd.ExecuteReader();var items=new List<CountRow>();while(rows.Read()){token.ThrowIfCancellationRequested();items.Add(new(Entry(rows),rows.GetString(10)));}return new CountPage(session,new(session.ExpectedCount,found,pending,wrong,extra,retired,unknown,readings,duplicates),total,clamped,items);
        });
    }
    /// <summary>Reads one page of raw attempts, duplicates, withdrawals and lifecycle actions without loading all audit evidence.</summary>
    internal DatabasePage<CountAction> Actions(long sessionId,int page=0,int size=50,CancellationToken token=default)
    {
        if(page<0 || size is <1 or >250)throw new ArgumentOutOfRangeException(nameof(size));return DatabaseRead.Run(history,token,scope=>
        {
            using var cmd=scope.Command();cmd.CommandText="SELECT COUNT(*) FROM count_actions WHERE session_id=$session";cmd.Parameters.AddWithValue("$session",sessionId);long total=Convert.ToInt64(cmd.ExecuteScalar());int clamped=(int)Math.Min(page,Math.Max(0,(total-1)/size));cmd.CommandText="SELECT id,revision,at_utc,action,entry_id,input_mode,input_value,result,resolution_json,note FROM count_actions WHERE session_id=$session ORDER BY revision DESC LIMIT $limit OFFSET $offset";cmd.Parameters.AddWithValue("$limit",size);cmd.Parameters.AddWithValue("$offset",clamped*size);using var reader=cmd.ExecuteReader();var rows=new List<CountAction>();while(reader.Read()){token.ThrowIfCancellationRequested();rows.Add(new(reader.GetInt64(0),reader.GetInt32(1),reader.GetString(2),reader.GetString(3),reader.IsDBNull(4)?null:reader.GetInt64(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8),reader.GetString(9)));}return new DatabasePage<CountAction>(total,clamped,rows);
        });
    }
    /// <summary>Streams every filtered result page, preserving frozen evidence and session context in quoted UTF-8/BOM CSV.</summary>
    internal long ExportCsv(string path,long sessionId,string result="All",string search="",CancellationToken token=default,IProgress<long>? progress=null)
    {
        return DatabaseRead.Run(history,token,scope=>
        {
            IEnumerable<IEnumerable<string>> Rows()
            {
                using var cmd=scope.Command();cmd.CommandText="SELECT "+SessionColumns+" FROM count_sessions WHERE id=$id";cmd.Parameters.AddWithValue("$id",sessionId);CountSession session;using(var reader=cmd.ExecuteReader()){if(!reader.Read())throw new InvalidDataException("Count session not found.");session=Session(reader);}cmd.Parameters.Clear();cmd.CommandText="SELECT "+SelectedEntry+","+ResultSql+Joined+"WHERE "+ResultsWhere(cmd,sessionId,result,search)+" ORDER BY e.id";using var rows=cmd.ExecuteReader();while(rows.Read()){token.ThrowIfCancellationRequested();var e=Entry(rows);var a=e.Snapshot;yield return new[]{session.Code,session.Name,session.Status,session.Location,session.CreatedUtc,session.ClosedUtc,e.AssetUid??"",a?.Code??"",a?.Serial??"",a?.AssetType??"",a?.Brand??"",a?.Model??"",a?.Location??"",e.Expected?"1":"0",e.Observed?"1":"0",rows.GetString(10),e.ObservedUtc,e.InputMode,e.InputValue,a?.Status??"",a?.Custodian??"",session.Notes,a?.UpdatedUtc??""};}
            }
            return CsvFile.Write(path,["SessionCode","SessionName","SessionStatus","ObservedLocation","SessionCreatedUtc","SessionClosedUtc","AssetUid","AssetCode","Serial","AssetType","Brand","Model","RecordedLocation","Expected","Observed","Outcome","ObservedUtc","InputMode","InputValue","RecordedAssetStatus","RecordedCustodian","SessionNotes","AssetSnapshotUpdatedUtc"],Rows(),token,progress);
        });
    }
    /// <summary>Streams immutable action evidence, including repeated and withdrawn readings, as a separate report.</summary>
    internal long ExportActionsCsv(string path,long sessionId,CancellationToken token=default,IProgress<long>? progress=null)=>DatabaseRead.Run(history,token,scope=>
    {
        IEnumerable<IEnumerable<string>> Rows(){using var cmd=scope.Command();cmd.CommandText="SELECT revision,at_utc,action,entry_id,input_mode,input_value,result,resolution_json,note FROM count_actions WHERE session_id=$session ORDER BY revision";cmd.Parameters.AddWithValue("$session",sessionId);using var reader=cmd.ExecuteReader();while(reader.Read()){token.ThrowIfCancellationRequested();var cells=new string[10];cells[0]=sessionId.ToString(CultureInfo.InvariantCulture);for(int i=0;i<9;i++)cells[i+1]=reader.IsDBNull(i)?"":Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture)!;yield return cells;}}
        return CsvFile.Write(path,["SessionId","Revision","AtUtc","Action","EntryId","InputMode","InputValue","Result","ResolutionJson","Note"],Rows(),token,progress);
    });
}
