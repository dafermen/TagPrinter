using System.Text.Json;
namespace TagPrinter;

/// <summary>Exact Dashboard/list navigation scope; null differs from an empty (unassigned) location or custodian.</summary>
internal sealed record AssetFilter(string Search="",string Status="",string? Type=null,string? Brand=null,string? Model=null,string? Location=null,string? Custodian=null,DateTimeOffset? FromUtc=null,DateTimeOffset? ToUtc=null,bool ExcludeRetired=false);
/// <summary>One canonical grouping with raw values retained for exact drill-down.</summary>
internal sealed record AssetGroup(string Type,string Brand,string Model,string Location,string Custodian,long Count);
internal sealed partial class AssetStore
{
    /// <summary>Counts the same exact scope used by list pages and Dashboard drill-down.</summary>
    internal long Count(AssetFilter filter)=>Count(filter.Search,filter.Status,filter.Type,filter.Brand,filter.Model,filter);
    /// <summary>Reads a bounded page under an exact navigation scope.</summary>
    internal List<AssetRecord> Query(AssetFilter filter,int offset=0,int limit=50,string sort="Updated",bool descending=true)=>Query(filter.Search,filter.Status,offset,limit,filter.Type,filter.Brand,filter.Model,sort,descending,filter);
    /// <summary>Streams a filtered master export without loading all rows into the UI.</summary>
    internal void ExportCsv(string path,AssetFilter filter)
    {
        IEnumerable<AssetRecord> Rows()
        {
            using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT record_json FROM assets WHERE "+Where(cmd,filter.Search,filter.Status,filter.Type,filter.Brand,filter.Model,filter)+" ORDER BY id";
            using var reader=cmd.ExecuteReader();while(reader.Read())yield return JsonSerializer.Deserialize<AssetRecord>(reader.GetString(0))!;
        }
        string absolute=Path.GetFullPath(path);if(absolute.Equals(history.DatabasePath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a CSV output path.");AssetCsv.Export(absolute,Rows());
    }
    /// <summary>Aggregates counts in SQL by asset description, location or custodian with matching filtered state totals.</summary>
    internal (List<AssetGroup> Groups,Dictionary<string,long> States) Overview(AssetFilter filter,string group)
    {
        using var c=history.Open();using var cmd=c.CreateCommand();string where=Where(cmd,filter.Search,filter.Status,filter.Type,filter.Brand,filter.Model,filter);
        string columns=group switch
        {
            "Location"=>"'','','',json_extract(record_json,'$.Location'),''",
            "Custodian"=>"'','','','',json_extract(record_json,'$.Custodian')",
            _=>"json_extract(record_json,'$.AssetType'),json_extract(record_json,'$.Brand'),json_extract(record_json,'$.Model'),'',''"
        };
        cmd.CommandText="SELECT "+columns+",COUNT(*) FROM assets WHERE "+where+" GROUP BY 1,2,3,4,5 ORDER BY 6 DESC,1,2,3,4,5";
        var rows=new List<AssetGroup>();using(var reader=cmd.ExecuteReader())while(reader.Read())rows.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt64(5)));
        cmd.CommandText="SELECT json_extract(record_json,'$.Status'),COUNT(*) FROM assets WHERE "+where+" GROUP BY 1";var states=new Dictionary<string,long>();using(var reader=cmd.ExecuteReader())while(reader.Read())states[reader.GetString(0)]=reader.GetInt64(1);return(rows,states);
    }
}
