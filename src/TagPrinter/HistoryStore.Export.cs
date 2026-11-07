namespace TagPrinter;

public sealed partial class HistoryStore
{
    internal static readonly string[] CsvHeaders=["Id","CreatedUtc","Action","Serial","AssetType","Brand","Model","PartNumber","MacAddress","Printer","Copies","CodeType","WidthMm","HeightMm","OutputPath","SourceEventId","StateJson"];
    /// <summary>Streams full-scope history from one SQLite snapshot without allocating/deserializing every saved design.</summary>
    internal long ExportAllCsv(string path,HistoryQuery query,CancellationToken token=default,IProgress<long>? progress=null)
    {
        return DatabaseRead.Run(this,token,scope=>
        {
            IEnumerable<IEnumerable<string>> Rows()
            {
                using var command=scope.Command();command.CommandText="SELECT id,created_utc,action,serial,asset_type,brand,model,part_number,mac_address,COALESCE(json_extract(state_json,'$.Printer'),''),COALESCE(json_extract(state_json,'$.Copies'),1),CASE WHEN action!='ExportCode' AND COALESCE(json_extract(state_json,'$.ShowCode'),1)=0 THEN 'TextOnly' WHEN json_extract(state_json,'$.Code')=1 THEN 'QR' ELSE 'Code128' END,COALESCE(json_extract(state_json,'$.Width'),100),COALESCE(json_extract(state_json,'$.Height'),50),output_path,source_event_id,state_json FROM events WHERE "+Where(command,query)+" ORDER BY id DESC";
                using var reader=command.ExecuteReader();while(reader.Read()){token.ThrowIfCancellationRequested();var cells=new string[17];for(int i=0;i<cells.Length;i++)cells[i]=reader.IsDBNull(i)?"":Convert.ToString(reader.GetValue(i),System.Globalization.CultureInfo.InvariantCulture)!;yield return cells;}
            }
            return CsvFile.Write(path,CsvHeaders,Rows(),token,progress);
        });
    }
}
