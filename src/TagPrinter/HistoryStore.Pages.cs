using System.Globalization;
namespace TagPrinter;

/// <summary>Lightweight report projection; the complete design is loaded only for explicit recovery or page export.</summary>
internal sealed record HistorySummary(long Id,DateTimeOffset CreatedUtc,string Action,string Serial,string AssetType,string Brand,string Model,string PartNumber,string MacAddress,string Printer,int Copies,string CodeType,long? SourceEventId);
public sealed partial class HistoryStore
{
    /// <summary>Counts and reads one consistent page without deserializing WorkState snapshots.</summary>
    internal DatabasePage<HistorySummary> ReadPage(HistoryQuery query,int page,int size,string sort,bool descending,CancellationToken token=default)
    {
        if(page<0 || size is <1 or >250)throw new ArgumentOutOfRangeException(nameof(size));
        return DatabaseRead.Run(this,token,scope=>
        {
            using var command=scope.Command();string where=Where(command,query);command.CommandText="SELECT COUNT(*) FROM events WHERE "+where;long total=Convert.ToInt64(command.ExecuteScalar());int clamped=(int)Math.Min(page,Math.Max(0,(total-1)/size));token.ThrowIfCancellationRequested();
            command.CommandText="SELECT id,created_utc,action,serial,asset_type,brand,model,part_number,mac_address,COALESCE(json_extract(state_json,'$.Printer'),''),COALESCE(CAST(json_extract(state_json,'$.Copies') AS INTEGER),1),CASE WHEN action!='ExportCode' AND COALESCE(json_extract(state_json,'$.ShowCode'),1)=0 THEN 'TextOnly' WHEN json_extract(state_json,'$.Code')=1 THEN 'QR' ELSE 'Code128' END,source_event_id FROM events WHERE "+where+" ORDER BY "+SortColumn(sort)+(descending?" DESC":" ASC")+",id DESC LIMIT $limit OFFSET $offset";command.Parameters.AddWithValue("$limit",size);command.Parameters.AddWithValue("$offset",clamped*size);
            using var reader=command.ExecuteReader();var rows=new List<HistorySummary>();while(reader.Read()){token.ThrowIfCancellationRequested();rows.Add(new(reader.GetInt64(0),DateTimeOffset.Parse(reader.GetString(1),CultureInfo.InvariantCulture),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetString(9),reader.GetInt32(10),reader.GetString(11),reader.IsDBNull(12)?null:reader.GetInt64(12)));}return new DatabasePage<HistorySummary>(total,clamped,rows);
        });
    }
    /// <summary>Loads at most one displayed page's designs in a single query, preserving requested display order.</summary>
    internal List<HistoryEvent> ReadSnapshots(IReadOnlyList<long> ids)
    {
        if(ids.Count>250)throw new ArgumentOutOfRangeException(nameof(ids));if(ids.Count==0)return [];
        using var connection=Open();using var command=connection.CreateCommand();var keys=ids.Select((id,i)=>"$id"+i).ToArray();for(int i=0;i<ids.Count;i++)command.Parameters.AddWithValue(keys[i],ids[i]);command.CommandText="SELECT id,created_utc,action,state_json,output_path,source_event_id,asset_uid FROM events WHERE id IN("+string.Join(',',keys)+")";using var reader=command.ExecuteReader();var map=new Dictionary<long,HistoryEvent>();while(reader.Read()){var row=Read(reader);map.Add(row.Id,row);}return ids.Where(map.ContainsKey).Select(id=>map[id]).ToList();
    }
}
