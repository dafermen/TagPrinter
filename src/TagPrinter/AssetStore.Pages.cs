using System.Text.Json;
namespace TagPrinter;

internal sealed partial class AssetStore
{
    /// <summary>Counts and reads a bounded canonical page from one read snapshot, with cancellable indexed predicates.</summary>
    internal DatabasePage<AssetRecord> ReadPage(AssetFilter filter,int page,int size,string sort,bool descending,CancellationToken token=default)
    {
        if(page<0 || size is <1 or >250)throw new ArgumentOutOfRangeException(nameof(size));
        return DatabaseRead.Run(history,token,scope=>
        {
            using var cmd=scope.Command();string where=Where(cmd,filter.Search,filter.Status,filter.Type,filter.Brand,filter.Model,filter);cmd.CommandText="SELECT COUNT(*) FROM assets WHERE "+where;long total=Convert.ToInt64(cmd.ExecuteScalar());int clamped=(int)Math.Min(page,Math.Max(0,(total-1)/size));token.ThrowIfCancellationRequested();
            cmd.CommandText="SELECT record_json FROM assets WHERE "+where+" ORDER BY "+SortColumn(sort)+(descending?" DESC":" ASC")+",id DESC LIMIT $limit OFFSET $offset";cmd.Parameters.AddWithValue("$limit",size);cmd.Parameters.AddWithValue("$offset",clamped*size);using var reader=cmd.ExecuteReader();var rows=new List<AssetRecord>();while(reader.Read()){token.ThrowIfCancellationRequested();rows.Add(JsonSerializer.Deserialize<AssetRecord>(reader.GetString(0))!);}return new DatabasePage<AssetRecord>(total,clamped,rows);
        });
    }
    /// <summary>Bounds audit snapshot loading even when one asset has a very long administrative history.</summary>
    internal DatabasePage<AssetMovement> ReadMovements(string uid,int page,int size=50,CancellationToken token=default)
    {
        if(page<0 || size is <1 or >250)throw new ArgumentOutOfRangeException(nameof(size));
        return DatabaseRead.Run(history,token,scope=>
        {
            using var cmd=scope.Command();cmd.Parameters.AddWithValue("$uid",uid);cmd.CommandText="SELECT COUNT(*) FROM asset_movements WHERE asset_uid=$uid";long total=Convert.ToInt64(cmd.ExecuteScalar());int clamped=(int)Math.Min(page,Math.Max(0,(total-1)/size));token.ThrowIfCancellationRequested();
            cmd.CommandText="SELECT id,at_utc,action,before_json,after_json,actor,note FROM asset_movements WHERE asset_uid=$uid ORDER BY id DESC LIMIT $limit OFFSET $offset";cmd.Parameters.AddWithValue("$limit",size);cmd.Parameters.AddWithValue("$offset",clamped*size);using var reader=cmd.ExecuteReader();var rows=new List<AssetMovement>();while(reader.Read()){token.ThrowIfCancellationRequested();rows.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:JsonSerializer.Deserialize<AssetRecord>(reader.GetString(3)),JsonSerializer.Deserialize<AssetRecord>(reader.GetString(4))!,reader.GetString(5)){Note=reader.GetString(6)});}return new DatabasePage<AssetMovement>(total,clamped,rows);
        });
    }
}
