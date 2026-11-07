using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

internal sealed partial class ServiceStore
{
    /// <summary>Checks service metadata, complete revision chains and foreign-key declarations before any restore replacement.</summary>
    internal static void ValidateDatabase(SqliteConnection c)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT id,asset_uid,kind,revision,due_date,status,record_json FROM asset_services";
        void Persisted(ServiceRecord row){Validate(row);if(row.Id<1 || row.Revision<1 || !DateTimeOffset.TryParseExact(row.CreatedUtc,"O",CultureInfo.InvariantCulture,DateTimeStyles.None,out var created) || !DateTimeOffset.TryParseExact(row.UpdatedUtc,"O",CultureInfo.InvariantCulture,DateTimeStyles.None,out var updated) || created.Offset!=TimeSpan.Zero || updated.Offset!=TimeSpan.Zero || updated<created)throw new InvalidDataException("Invalid service timestamps/identity.");}
        using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            var row=JsonSerializer.Deserialize<ServiceRecord>(reader.GetString(6))??throw new InvalidDataException("Missing service record.");Persisted(row);if(row.Id!=reader.GetInt64(0) || row.AssetUid!=reader.GetString(1) || row.Kind!=reader.GetString(2) || row.Revision!=reader.GetInt32(3) || row.DueDate!=reader.GetString(4) || row.Status!=reader.GetString(5))throw new InvalidDataException("Service metadata does not match its snapshot.");
        }
        cmd.CommandText="SELECT COUNT(*) FROM asset_services s WHERE NOT EXISTS(SELECT 1 FROM service_changes c WHERE c.service_id=s.id)";if(Convert.ToInt64(cmd.ExecuteScalar())!=0)throw new InvalidDataException("Missing service revision history.");
        cmd.CommandText="SELECT c.service_id,c.at_utc,c.before_json,c.after_json,s.record_json FROM service_changes c JOIN asset_services s ON s.id=c.service_id ORDER BY c.service_id,c.id";ServiceRecord? previous=null,current=null;
        using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            var after=JsonSerializer.Deserialize<ServiceRecord>(reader.GetString(3))??throw new InvalidDataException("Missing service revision.");var before=reader.IsDBNull(2)?null:JsonSerializer.Deserialize<ServiceRecord>(reader.GetString(2));Persisted(after);
            if(previous?.Id!=reader.GetInt64(0)){if(previous is not null && previous!=current)throw new InvalidDataException("Service latest revision mismatch.");previous=null;current=JsonSerializer.Deserialize<ServiceRecord>(reader.GetString(4));}
            if(after.Id!=reader.GetInt64(0) || after.UpdatedUtc!=reader.GetString(1) || before!=previous || after.Revision!=(previous?.Revision??0)+1 || (previous is not null && (after.AssetUid!=previous.AssetUid || after.Kind!=previous.Kind || after.CreatedUtc!=previous.CreatedUtc)))throw new InvalidDataException("Broken service revision chain.");previous=after;
        }
        if(previous is not null && previous!=current)throw new InvalidDataException("Service latest revision mismatch.");
        foreach(var spec in new[]{("asset_services","asset_uid","assets","uid"),("service_changes","service_id","asset_services","id")})
        {
            cmd.CommandText="PRAGMA foreign_key_list("+spec.Item1+")";bool valid=false;using(var reader=cmd.ExecuteReader())while(reader.Read())if(reader.GetString(2)==spec.Item3 && reader.GetString(3)==spec.Item2 && reader.GetString(4)==spec.Item4)valid=true;if(!valid)throw new InvalidDataException("Missing service foreign key.");
        }
    }
}
