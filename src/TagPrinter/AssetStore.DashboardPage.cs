namespace TagPrinter;

/// <summary>Bounded group page plus full-scope state and label totals from one consistent read snapshot.</summary>
internal sealed record DashboardPage(long TotalGroups,int Page,List<AssetGroup> Groups,Dictionary<string,long> States,long Exports,long Jobs,long Copies);
internal sealed partial class AssetStore
{
    /// <summary>Counts aggregate groups in SQL and materializes only one page; cards remain full-scope counts.</summary>
    internal DashboardPage ReadDashboard(AssetFilter filter,string group,int page=0,int size=50,CancellationToken token=default)
    {
        if(page<0 || size is <1 or >250)throw new ArgumentOutOfRangeException(nameof(size));
        return DatabaseRead.Run(history,token,scope=>
        {
            using var cmd=scope.Command();string where=Where(cmd,filter.Search,filter.Status,filter.Type,filter.Brand,filter.Model,filter);
            string columns=group switch{"Location"=>"'','','',json_extract(record_json,'$.Location'),''","Custodian"=>"'','','','',json_extract(record_json,'$.Custodian')",_=>"json_extract(record_json,'$.AssetType'),json_extract(record_json,'$.Brand'),json_extract(record_json,'$.Model'),'',''"};
            string groups="SELECT "+columns+",COUNT(*) FROM assets WHERE "+where+" GROUP BY 1,2,3,4,5";
            cmd.CommandText="SELECT COUNT(*) FROM ("+groups+")";long total=Convert.ToInt64(cmd.ExecuteScalar());int clamped=(int)Math.Min(page,Math.Max(0,(total-1)/size));token.ThrowIfCancellationRequested();
            cmd.CommandText=groups+" ORDER BY 6 DESC,1,2,3,4,5 LIMIT $limit OFFSET $offset";cmd.Parameters.AddWithValue("$limit",size);cmd.Parameters.AddWithValue("$offset",clamped*size);
            var rows=new List<AssetGroup>();using(var reader=cmd.ExecuteReader())while(reader.Read()){token.ThrowIfCancellationRequested();rows.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt64(5)));}
            cmd.CommandText="SELECT json_extract(record_json,'$.Status'),COUNT(*) FROM assets WHERE "+where+" GROUP BY 1";var states=new Dictionary<string,long>();using(var reader=cmd.ExecuteReader())while(reader.Read())states.Add(reader.GetString(0),reader.GetInt64(1));token.ThrowIfCancellationRequested();
            cmd.Parameters.Clear();cmd.CommandText="SELECT COALESCE(SUM(action='ExportTag'),0),COALESCE(SUM(action='PrintSubmitted'),0),COALESCE(SUM(CASE WHEN action='PrintSubmitted' THEN CAST(json_extract(state_json,'$.Copies') AS INTEGER) ELSE 0 END),0) FROM events WHERE "+DatabasePerformance.InventoryEvent+" AND id NOT IN(SELECT event_id FROM trash)";
            using var totals=cmd.ExecuteReader();totals.Read();return new DashboardPage(total,clamped,rows,states,totals.GetInt64(0),totals.GetInt64(1),totals.GetInt64(2));
        });
    }
}
