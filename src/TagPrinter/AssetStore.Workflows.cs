using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace TagPrinter;

internal sealed partial class AssetStore
{
    /// <summary>Provides a read-only vocabulary snapshot for deterministic import review.</summary>
    internal AssetCatalog.PreviewNames OpenPreviewNames()=>new AssetCatalog(history).OpenPreview();
    /// <summary>Finds exactly one canonical serial, including retired records, without mutating the label draft.</summary>
    internal AssetRecord? FindBySerial(string serial)
    {
        if(string.IsNullOrWhiteSpace(serial))return null;
        using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT record_json FROM assets WHERE serial_key!='' AND serial_key=$key";cmd.Parameters.AddWithValue("$key",SerialKey(serial));return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<AssetRecord>(json):null;
    }
    /// <summary>Performs an explicit business action with required assignment/destination data and an atomic movement reason.</summary>
    internal AssetRecord Transition(AssetRecord source,string action,string location,string custodian,string note)
    {
        if(source.Status=="Retired")throw new InvalidOperationException(Ui.T("Reactiva el activo desde Editar antes de asignarlo o moverlo.","Reactivate the asset in Edit before assigning or moving it."));
        if(string.IsNullOrWhiteSpace(location))throw new InvalidDataException(Ui.T("Indica la ubicación de destino.","Enter the destination location."));
        var target=action switch
        {
            "Assigned"=>source with {Status="Assigned",Location=location,Custodian=custodian},
            "Returned"=>source with {Status="Available",Location=location,Custodian=""},
            "RepairStarted"=>source with {Status="Repair",Location=location,Custodian=custodian},
            _=>throw new ArgumentException("Unknown asset action.")
        };
        using var c=history.Open();using var tx=c.BeginTransaction();var saved=Save(c,tx,target,action,note);tx.Commit();return saved;
    }
    /// <summary>Commits a previously reviewed administrative CSV plan, rejecting stale/duplicate targets as one transaction.</summary>
    internal (int Created,int Updated,int Unchanged) ImportAssets(IReadOnlyList<AssetImportRow> plan)
    {
        if(plan.Count is <1 or >10000)throw new InvalidDataException("Invalid asset import size.");
        using var c=history.Open();using var tx=c.BeginTransaction();int created=0,updated=0,unchanged=0;
        var targets=new HashSet<string>();var serials=new HashSet<string>();
        foreach(var item in plan)
        {
            if(item.After.Serial.Length>0 && !serials.Add(SerialKey(item.After.Serial)))throw new InvalidDataException("Repeated serial in asset import.");
            if(item.Before is {} before)
            {
                if(!targets.Add(before.Uid) || item.After.Uid!=before.Uid || item.After.Revision!=before.Revision)throw new InvalidDataException("Repeated or invalid asset import target.");
                var current=Get(c,tx,before.Uid);if(current!=before)throw new InvalidOperationException(Ui.T("El inventario cambió desde la vista previa. Vuelve a importar el archivo.","Inventory changed after preview. Import the file again."));
            }
            else if(item.After.Uid.Length>0 || item.After.Revision!=0)throw new InvalidDataException("Invalid new asset import identity.");
            if(AssetCatalog.Normalize(c,tx,item.After)!=item.After)throw new InvalidOperationException(Ui.T("El catálogo cambió desde la vista previa. Revisa el archivo de nuevo.","Catalog changed after preview. Review the file again."));
            var saved=Save(c,tx,item.After,item.Before is null?"Imported":"ImportUpdated");
            if(item.Before is null)created++;else if(saved.Revision==item.Before.Revision)unchanged++;else updated++;
        }
        tx.Commit();return (created,updated,unchanged);
    }
}
