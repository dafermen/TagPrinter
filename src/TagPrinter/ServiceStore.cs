using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

/// <summary>Transactional local service records and revision history, covered by the existing SQLite backup.</summary>
internal sealed partial class ServiceStore
{
    private readonly HistoryStore history;
    internal static readonly string[] MaintenanceStates=["Planned","InProgress","Completed","Canceled"];
    internal static readonly string[] WarrantyStates=["Active","Claimed","Closed"];
    /// <summary>Shares the inventory file; it never creates a separate service database.</summary>
    internal ServiceStore(HistoryStore history){this.history=history;}
    /// <summary>Creates schema 4 atomically. Existing asset and label snapshots are not rewritten.</summary>
    internal static void Initialize(SqliteConnection connection)
    {
        using var tx=connection.BeginTransaction();using var cmd=connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS asset_services(id INTEGER PRIMARY KEY AUTOINCREMENT,asset_uid TEXT NOT NULL REFERENCES assets(uid),kind TEXT NOT NULL CHECK(kind IN ('Maintenance','Warranty')),revision INTEGER NOT NULL CHECK(revision>0),due_date TEXT NOT NULL,status TEXT NOT NULL,record_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_services_due ON asset_services(kind,due_date,id);
            CREATE INDEX IF NOT EXISTS ix_services_asset ON asset_services(asset_uid,kind,due_date,id);
            CREATE INDEX IF NOT EXISTS ix_services_status ON asset_services(kind,status,due_date,id);
            CREATE TABLE IF NOT EXISTS service_changes(id INTEGER PRIMARY KEY AUTOINCREMENT,service_id INTEGER NOT NULL REFERENCES asset_services(id),at_utc TEXT NOT NULL,before_json TEXT,after_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_service_changes ON service_changes(service_id,id);
            PRAGMA user_version=4;
            """;cmd.ExecuteNonQuery();tx.Commit();
    }
    /// <summary>Returns one fresh revision, including archived records.</summary>
    internal ServiceRecord? Get(long id){using var c=history.Open();return Get(c,null,id);}
    /// <summary>Reads inside the caller's write transaction to detect stale editors.</summary>
    private static ServiceRecord? Get(SqliteConnection c,SqliteTransaction? tx,long id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT record_json FROM asset_services WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<ServiceRecord>(json):null;
    }
    /// <summary>Saves a record and its audit snapshot together. Jobs never silently alter the asset's assignment/state.</summary>
    internal ServiceRecord Save(ServiceRecord record)
    {
        var candidate=record with {AssetUid=Guid.TryParse(record.AssetUid,out var guid)?guid.ToString("N"):record.AssetUid,Title=record.Title.Trim(),Provider=record.Provider.Trim(),Reference=record.Reference.Trim(),Currency=record.Currency.Trim().ToUpperInvariant(),Notes=record.Notes.Trim()};Validate(candidate);
        using var c=history.Open();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT COUNT(*) FROM assets WHERE uid=$uid";cmd.Parameters.AddWithValue("$uid",candidate.AssetUid);if(Convert.ToInt32(cmd.ExecuteScalar())!=1)throw new InvalidDataException(Ui.T("El activo ya no existe.","The asset no longer exists."));cmd.Parameters.Clear();
        var before=candidate.Id==0?null:Get(c,tx,candidate.Id);
        if(candidate.Id!=0 && before is null)throw new InvalidOperationException(Ui.T("El registro ya no existe.","The record no longer exists."));
        if(before is not null && (before.Revision!=candidate.Revision || before.AssetUid!=candidate.AssetUid || before.Kind!=candidate.Kind))throw new InvalidOperationException(Ui.T("El registro cambió. Recárgalo antes de guardar.","The record changed. Reload it before saving."));
        if(before is null && candidate.Revision!=0)throw new InvalidDataException("A new service cannot have a revision.");
        if(before is not null && candidate with {CreatedUtc=before.CreatedUtc,UpdatedUtc=before.UpdatedUtc}==before)return before;
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);candidate=candidate with {Revision=(before?.Revision??0)+1,CreatedUtc=before?.CreatedUtc??now,UpdatedUtc=now};
        cmd.CommandText=before is null?"INSERT INTO asset_services(asset_uid,kind,revision,due_date,status,record_json) VALUES($uid,$kind,$revision,$date,$status,$json);SELECT last_insert_rowid();":"UPDATE asset_services SET revision=$revision,due_date=$date,status=$status,record_json=$json WHERE id=$id AND revision=$previous;SELECT changes();";
        cmd.Parameters.AddWithValue("$uid",candidate.AssetUid);cmd.Parameters.AddWithValue("$kind",candidate.Kind);cmd.Parameters.AddWithValue("$revision",candidate.Revision);cmd.Parameters.AddWithValue("$date",candidate.DueDate);cmd.Parameters.AddWithValue("$status",candidate.Status);cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(candidate));cmd.Parameters.AddWithValue("$id",candidate.Id);cmd.Parameters.AddWithValue("$previous",before?.Revision??0);long changed=Convert.ToInt64(cmd.ExecuteScalar());
        if(before is not null && changed!=1)throw new InvalidOperationException("Service changed during save.");
        if(before is null){candidate=candidate with {Id=changed};cmd.CommandText="UPDATE asset_services SET record_json=$json WHERE id=$id";cmd.Parameters["$id"].Value=candidate.Id;cmd.Parameters["$json"].Value=JsonSerializer.Serialize(candidate);cmd.ExecuteNonQuery();}
        cmd.Parameters.Clear();cmd.CommandText="INSERT INTO service_changes(service_id,at_utc,before_json,after_json) VALUES($id,$at,$before,$after)";cmd.Parameters.AddWithValue("$id",candidate.Id);cmd.Parameters.AddWithValue("$at",now);cmd.Parameters.AddWithValue("$before",before is null?DBNull.Value:JsonSerializer.Serialize(before));cmd.Parameters.AddWithValue("$after",JsonSerializer.Serialize(candidate));cmd.ExecuteNonQuery();tx.Commit();return candidate;
    }
    /// <summary>Validates calendar dates independently of Windows formatting and rejects inconsistent workflow/currency data.</summary>
    internal static void Validate(ServiceRecord r)
    {
        void Text(string? value,int max){if(value is null || value.Length>max || value.Contains('\0'))throw new InvalidDataException(Ui.T("Campo de mantenimiento o garantía inválido.","Invalid maintenance or warranty field."));}
        Text(r.AssetUid,32);Text(r.Title,160);Text(r.Provider,160);Text(r.Reference,120);Text(r.Notes,4000);Text(r.Currency,3);
        if(!Guid.TryParseExact(r.AssetUid,"N",out _) || string.IsNullOrWhiteSpace(r.Title) || r.Id<0 || r.Revision<0)throw new InvalidDataException(Ui.T("Selecciona un activo e indica una descripción.","Choose an asset and enter a description."));
        DateOnly? Date(string? value,bool required=false){if(value=="" && !required)return null;if(value is null || !DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d) || d.Year<1900 || d.Year>9998)throw new InvalidDataException(Ui.T("Revisa las fechas (1900–9998).","Check the dates (1900–9998)."));return d;}
        var due=Date(r.DueDate,true);var start=Date(r.StartDate,r.Kind=="Warranty");var completed=Date(r.CompletedDate);
        if(r.Kind=="Maintenance")
        {
            if(!MaintenanceStates.Contains(r.Status) || r.Category is not ("Preventive" or "Corrective") || (r.Status=="Completed")!=(completed is not null) || (completed is not null && start>completed))throw new InvalidDataException(Ui.T("Revisa el estado y la fecha de finalización del mantenimiento.","Check maintenance status and completion date."));
            if(r.Cost is {} cost && (cost<0 || cost>999999999.99m || decimal.Round(cost,2)!=cost || r.Currency.Length!=3))throw new InvalidDataException(Ui.T("Indica un costo no negativo con hasta dos decimales y una moneda de tres letras.","Enter a nonnegative amount with up to two decimals and a three-letter currency."));
            if(r.Currency.Any(ch=>ch is <'A' or >'Z') || (r.Cost is null && r.Currency.Length>0))throw new InvalidDataException(Ui.T("La moneda requiere un costo y tres letras, por ejemplo USD o COP.","Currency requires an amount and three letters, such as USD or COP."));
        }
        else if(r.Kind!="Warranty" || !WarrantyStates.Contains(r.Status) || r.Category!="" || r.CompletedDate!="" || r.Cost is not null || r.Currency!="" || start>due)throw new InvalidDataException(Ui.T("Revisa las fechas y el estado de la garantía.","Check warranty dates and status."));
    }
    /// <summary>Translates stable workflow/category keys; user descriptions are never translated.</summary>
    internal static string Caption(string key)=>key switch
    {
        "Maintenance"=>Ui.T("Mantenimientos","Maintenance"),"Warranty"=>Ui.T("Garantías","Warranties"),"Planned"=>Ui.T("Programado","Planned"),"InProgress"=>Ui.T("En curso","In progress"),"Completed"=>Ui.T("Finalizado","Completed"),"Canceled"=>Ui.T("Cancelado","Canceled"),"Active"=>Ui.T("Activa","Active"),"Claimed"=>Ui.T("Reclamación abierta","Claim open"),"Closed"=>Ui.T("Cerrada","Closed"),"Preventive"=>Ui.T("Preventivo","Preventive"),"Corrective"=>Ui.T("Correctivo","Corrective"),"All"=>Ui.T("Todas las fechas","All dates"),"Attention"=>Ui.T("Requiere atención","Needs attention"),"Late"=>Ui.T("Atrasado / vencido","Overdue / expired"),"Upcoming"=>Ui.T("Próximos vencimientos","Upcoming deadlines"),_=>key
    };
    /// <summary>Uses the local calendar; a warranty is still in period on its expiration day.</summary>
    internal static string Alert(ServiceRecord r,DateOnly today,int days)
    {
        if(r.Status is "Completed" or "Canceled" or "Closed")return Caption(r.Status);var due=DateOnly.ParseExact(r.DueDate,"yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(due<today)return r.Kind=="Warranty"?Ui.T("Vencida","Expired"):Ui.T("Atrasado","Overdue");
        if(r.Kind=="Warranty" && DateOnly.ParseExact(r.StartDate,"yyyy-MM-dd",CultureInfo.InvariantCulture)>today)return Ui.T("Aún no inicia","Not started");
        if(due<=today.AddDays(days))return r.Kind=="Warranty"?Ui.T("Por vencer","Expiring soon"):Ui.T("Próximo","Due soon");return r.Kind=="Warranty"?Ui.T("En vigencia","In period"):Caption(r.Status);
    }
}
