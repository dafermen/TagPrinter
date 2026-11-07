using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.Text;
namespace TagPrinter;

/// <summary>Reviewed administrative import row; Before captures concurrency state, After contains proposed full business values.</summary>
internal sealed record AssetImportRow(int Record,AssetRecord? Before,AssetRecord After);
/// <summary>Administrative semicolon CSV. This format edits master records and never generates label events or print jobs.</summary>
internal static class AssetCsv
{
    internal static readonly string[] Fields=["AssetType","Brand","Model","Serial","PartNumber","MacAddress","Status","Location","Custodian","Notes"];
    internal static readonly string[] Headers=["AssetUid","AssetCode","Revision",..Fields];
    /// <summary>Uses invariant keys in files and translated field labels in review/audit tables.</summary>
    internal static string Caption(string field)=>field switch
    {
        "AssetType"=>Ui.T("Tipo de activo","Asset type"),"Brand"=>Ui.T("Marca","Brand"),"Model"=>Ui.T("Modelo","Model"),"PartNumber"=>Ui.T("No. de parte","Part number"),"MacAddress"=>"MAC","Status"=>Ui.T("Estado","Status"),"Location"=>Ui.T("Ubicación","Location"),"Custodian"=>Ui.T("Responsable","Custodian"),"Notes"=>Ui.T("Observaciones","Notes"),_=>field
    };
    /// <summary>Gets business field values without serializing metadata into user-facing tables.</summary>
    internal static string Value(AssetRecord row,string field)=>(string)typeof(AssetRecord).GetProperty(field)!.GetValue(row)!;
    /// <summary>Lists actual proposed field differences, including explicit blank values.</summary>
    internal static IEnumerable<(string Field,string Before,string After)> Changes(AssetImportRow row)
    {
        foreach(string field in Fields){string before=row.Before is null?"":Value(row.Before,field),after=Value(row.After,field);if(before!=after)yield return(field,before,after);}
    }
    /// <summary>Validates the entire UTF-8 file before returning a plan; unknown identities/revisions/duplicate targets reject the file.</summary>
    internal static List<AssetImportRow> Read(string path,AssetStore store)
    {
        if(new FileInfo(path).Length>10*1024*1024)throw new InvalidDataException("Asset CSV exceeds 10 MB.");
        using var parser=new TextFieldParser(path,new UTF8Encoding(false,true)){HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};parser.SetDelimiters(";");
        var headers=parser.ReadFields()??throw new InvalidDataException("CSV header missing.");
        if(headers.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=headers.Length || Fields.Any(f=>!headers.Contains(f,StringComparer.OrdinalIgnoreCase)))throw new InvalidDataException(Ui.T("Usa la plantilla CSV administrativa; faltan columnas o están repetidas.","Use the administrative CSV template; columns are missing or repeated."));
        var targets=new HashSet<string>();var serials=new HashSet<string>();var plan=new List<AssetImportRow>();var vocabulary=store.CatalogSnapshot();
        while(!parser.EndOfData)
        {
            var cells=parser.ReadFields()!;int record=plan.Count+1;if(cells.Length!=headers.Length)throw new InvalidDataException($"CSV record {record}: column count mismatch.");
            string V(string key){int index=Array.FindIndex(headers,h=>h.Equals(key,StringComparison.OrdinalIgnoreCase));return index<0?"":cells[index].Trim();}
            try
            {
                string uid=V("AssetUid"),serial=V("Serial");AssetRecord? before=uid.Length>0?store.Get(uid):store.FindBySerial(serial);
                if(uid.Length>0 && before is null)throw new InvalidDataException(Ui.T("El código pertenece a otra base o el activo no existe. Para crear uno nuevo, deja vacíos AssetUid, AssetCode y Revision.","Identity is from another database or asset is missing. To create a new asset, leave AssetUid, AssetCode and Revision blank."));
                if(uid.Length>0 && V("Revision").Length==0)throw new InvalidDataException("Keep the exported Revision when updating by AssetUid.");
                if(V("AssetCode").Length>0 && (uid.Length==0 || before?.Code!=V("AssetCode")))throw new InvalidDataException("AssetCode requires its matching AssetUid.");
                if(V("Revision").Length>0 && (!int.TryParse(V("Revision"),NumberStyles.None,CultureInfo.InvariantCulture,out int revision) || before is null || before.Revision!=revision))throw new InvalidDataException(Ui.T("La revisión del activo cambió. Exporta de nuevo antes de actualizarlo.","Asset revision changed. Export again before updating."));
                var after=(before??new AssetRecord()) with {AssetType=V("AssetType"),Brand=V("Brand"),Model=V("Model"),Serial=serial,PartNumber=V("PartNumber"),MacAddress=V("MacAddress"),Status=V("Status"),Location=V("Location"),Custodian=V("Custodian"),Notes=V("Notes")};AssetStore.Validate(after);after=AssetCatalog.Preview(after,vocabulary);
                if(before is not null && !targets.Add(before.Uid))throw new InvalidDataException("Repeated asset target.");
                if(serial.Length>0)
                {
                    if(!serials.Add(AssetCatalog.Key(serial)))throw new InvalidDataException("Repeated serial.");
                    var owner=store.FindBySerial(serial);if(owner is not null && owner.Uid!=before?.Uid)throw new InvalidDataException("Serial belongs to another asset.");
                }
                plan.Add(new(record,before,after));if(plan.Count>10000)throw new InvalidDataException("At most 10000 assets per CSV.");
            }
            catch(Exception error)when(error is InvalidDataException or ArgumentException){throw new InvalidDataException($"CSV record {record}: {error.Message}",error);}
        }
        if(plan.Count==0)throw new InvalidDataException("CSV contains no assets.");return plan;
    }
    /// <summary>Writes quoted UTF-8/BOM cells, preserving semicolons, line breaks and leading-zero identifiers.</summary>
    internal static void Export(string path,IEnumerable<AssetRecord> rows)
    {
        if(!Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a .csv output file.");
        using var writer=new StreamWriter(path,false,new UTF8Encoding(true));
        void Write(IEnumerable<string> cells)=>writer.WriteLine(string.Join(";",cells.Select(v=>"\""+v.Replace("\"","\"\"")+"\"")));
        Write(Headers);foreach(var row in rows)Write(new[]{row.Uid,row.Uid.Length==0?"":row.Code,row.Revision==0?"":row.Revision.ToString(CultureInfo.InvariantCulture)}.Concat(Fields.Select(f=>Value(row,f))));
    }
}
