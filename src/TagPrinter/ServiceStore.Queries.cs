using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

internal sealed partial class ServiceStore
{
    // This join reads live asset captions. The foreign key remains stable through serial and model edits.
    private const string Joined=" FROM asset_services s JOIN assets a ON a.uid=s.asset_uid ";
    /// <summary>Builds bound search/state/date predicates. SQL fragments come only from internal whitelists.</summary>
    private static string Where(SqliteCommand cmd,ServiceFilter filter,DateOnly today,bool deadline=true)
    {
        if(filter.Kind is not ("Maintenance" or "Warranty") || filter.Days is <1 or >365 || filter.Deadline is not ("All" or "Attention" or "Late" or "Upcoming"))throw new ArgumentException("Invalid service filter.");
        string where="s.kind=$kind";cmd.Parameters.AddWithValue("$kind",filter.Kind);cmd.Parameters.AddWithValue("$today",today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$until",today.AddDays(filter.Days).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        if(filter.AssetUid is not null){where+=" AND s.asset_uid=$uid";cmd.Parameters.AddWithValue("$uid",filter.AssetUid);}
        if(!filter.IncludeRetired)where+=" AND json_extract(a.record_json,'$.Status')!='Retired'";
        if(filter.Status.Length>0){where+=" AND s.status=$state";cmd.Parameters.AddWithValue("$state",filter.Status);}
        if(filter.Search.Length>0)
        {
            where+=" AND (printf('TP-%08d',a.id)||char(10)||COALESCE(json_extract(a.record_json,'$.Serial'),'')||char(10)||COALESCE(json_extract(a.record_json,'$.AssetType'),'')||char(10)||COALESCE(json_extract(a.record_json,'$.Brand'),'')||char(10)||COALESCE(json_extract(a.record_json,'$.Model'),'')||char(10)||json_extract(s.record_json,'$.Title')||char(10)||json_extract(s.record_json,'$.Provider')||char(10)||json_extract(s.record_json,'$.Reference')||char(10)||json_extract(s.record_json,'$.Notes')) LIKE $search ESCAPE '~'";
            cmd.Parameters.AddWithValue("$search","%"+filter.Search.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_")+"%");
        }
        if(deadline)where+=filter.Deadline switch{"Attention"=>" AND "+Open+" AND s.due_date<=$until","Late"=>" AND "+Open+" AND s.due_date<$today","Upcoming"=>" AND "+Open+" AND s.due_date>=$today AND s.due_date<=$until",_=>""};return where;
    }
    private const string Open="s.status NOT IN ('Completed','Canceled','Closed')";
    /// <summary>Counts and projects one page inside a cancellable WAL snapshot; alert totals ignore only the deadline filter.</summary>
    internal ServicePage ReadPage(ServiceFilter filter,int page=0,int size=50,string sort="Due",bool descending=false,DateOnly? day=null,CancellationToken token=default)
    {
        if(page<0 || size is <1 or >250)throw new ArgumentOutOfRangeException(nameof(size));var today=day??DateOnly.FromDateTime(DateTime.Today);
        return DatabaseRead.Run(history,token,scope=>
        {
            using var cmd=scope.Command();string basic=Where(cmd,filter,today,false);cmd.CommandText="SELECT COALESCE(SUM("+Open+" AND s.due_date<$today),0),COALESCE(SUM("+Open+" AND s.due_date>=$today AND s.due_date<=$until),0)"+Joined+"WHERE "+basic;long late,upcoming;using(var reader=cmd.ExecuteReader()){reader.Read();late=reader.GetInt64(0);upcoming=reader.GetInt64(1);}cmd.Parameters.Clear();string where=Where(cmd,filter,today);cmd.CommandText="SELECT COUNT(*)"+Joined+"WHERE "+where;long total=Convert.ToInt64(cmd.ExecuteScalar());int clamped=(int)Math.Min(page,Math.Max(0,(total-1)/size));token.ThrowIfCancellationRequested();
            cmd.CommandText="SELECT s.record_json,a.id,json_extract(a.record_json,'$.AssetType'),json_extract(a.record_json,'$.Brand'),json_extract(a.record_json,'$.Model'),json_extract(a.record_json,'$.Serial'),json_extract(a.record_json,'$.Status')"+Joined+"WHERE "+where+" ORDER BY "+Sort(sort)+(descending?" DESC":" ASC")+",s.id ASC LIMIT $limit OFFSET $offset";cmd.Parameters.AddWithValue("$limit",size);cmd.Parameters.AddWithValue("$offset",clamped*size);using var rows=cmd.ExecuteReader();var result=new List<ServiceRow>();while(rows.Read()){token.ThrowIfCancellationRequested();result.Add(Row(rows));}return new ServicePage(total,clamped,result,late,upcoming);
        });
    }
    /// <summary>Only known column names can change SQL ordering; display captions never enter SQL.</summary>
    private static string Sort(string key)=>key switch{"Code"=>"s.id","Asset"=>"a.id","Serial"=>"json_extract(a.record_json,'$.Serial') COLLATE NOCASE","Title"=>"json_extract(s.record_json,'$.Title') COLLATE NOCASE","Status"=>"s.status","Provider"=>"json_extract(s.record_json,'$.Provider') COLLATE NOCASE",_=>"s.due_date"};
    /// <summary>Deserializes one bounded record while retaining current descriptive asset columns.</summary>
    private static ServiceRow Row(SqliteDataReader reader)=>new(JsonSerializer.Deserialize<ServiceRecord>(reader.GetString(0))!,"TP-"+reader.GetInt64(1).ToString("D8",CultureInfo.InvariantCulture),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6));
    /// <summary>Streams every filtered record into an atomic CSV destination with invariant dates/amounts and explicit currency.</summary>
    internal long ExportCsv(string path,ServiceFilter filter,CancellationToken token=default,IProgress<long>? progress=null)
    {
        return DatabaseRead.Run(history,token,scope=>
        {
            IEnumerable<IEnumerable<string>> Rows()
            {
                using var cmd=scope.Command();cmd.CommandText="SELECT s.record_json,a.id,json_extract(a.record_json,'$.AssetType'),json_extract(a.record_json,'$.Brand'),json_extract(a.record_json,'$.Model'),json_extract(a.record_json,'$.Serial'),json_extract(a.record_json,'$.Status')"+Joined+"WHERE "+Where(cmd,filter,DateOnly.FromDateTime(DateTime.Today))+" ORDER BY s.due_date,s.id";using var reader=cmd.ExecuteReader();while(reader.Read()){token.ThrowIfCancellationRequested();var row=Row(reader);var s=row.Record;yield return new[]{s.Id.ToString(CultureInfo.InvariantCulture),s.Code,s.AssetUid,row.AssetCode,row.AssetType,row.Brand,row.Model,row.Serial,s.Kind,s.Title,s.Category,s.Status,s.StartDate,s.DueDate,s.CompletedDate,s.Provider,s.Reference,s.Cost?.ToString("0.00",CultureInfo.InvariantCulture)??"",s.Currency,s.Notes,s.CreatedUtc,s.UpdatedUtc,s.Revision.ToString(CultureInfo.InvariantCulture)};}
            }
            return CsvFile.Write(path,["Id","Code","AssetUid","AssetCode","AssetType","Brand","Model","Serial","Kind","Title","Category","Status","StartDate","DueDate","CompletedDate","Provider","Reference","Cost","Currency","Notes","CreatedUtc","UpdatedUtc","Revision"],Rows(),token,progress);
        });
    }
    /// <summary>Reads only one page of immutable before/after revisions for a selected service record.</summary>
    internal DatabasePage<ServiceChange> ReadChanges(long id,int page=0,int size=50,CancellationToken token=default)
    {
        if(page<0 || size is <1 or >250)throw new ArgumentOutOfRangeException(nameof(size));return DatabaseRead.Run(history,token,scope=>
        {
            using var cmd=scope.Command();cmd.CommandText="SELECT COUNT(*) FROM service_changes WHERE service_id=$id";cmd.Parameters.AddWithValue("$id",id);long total=Convert.ToInt64(cmd.ExecuteScalar());int clamped=(int)Math.Min(page,Math.Max(0,(total-1)/size));cmd.CommandText="SELECT id,at_utc,before_json,after_json FROM service_changes WHERE service_id=$id ORDER BY id DESC LIMIT $limit OFFSET $offset";cmd.Parameters.AddWithValue("$limit",size);cmd.Parameters.AddWithValue("$offset",clamped*size);using var reader=cmd.ExecuteReader();var rows=new List<ServiceChange>();while(reader.Read()){token.ThrowIfCancellationRequested();rows.Add(new(reader.GetInt64(0),reader.GetString(1),reader.IsDBNull(2)?null:JsonSerializer.Deserialize<ServiceRecord>(reader.GetString(2)),JsonSerializer.Deserialize<ServiceRecord>(reader.GetString(3))!));}return new DatabasePage<ServiceChange>(total,clamped,rows);
        });
    }
}
