using System.Text.Json;
namespace TagPrinter;
public record WorkState
{
    public string Printer {get;init;}="";
    public int Size {get;init;}=0;
    public decimal Width {get;init;}=100;
    public decimal Height {get;init;}=50;
    public int Layout {get;init;}=3;
    public int Code {get;init;}
    public int Mode {get;init;}
    public int Dpi {get;init;}
    public string AssetType {get;init;}="";
    public bool ShowAssetType {get;init;}=true;
    public string Brand {get;init;}="Dell";
    public string Model {get;init;}="22\" - H22203H";
    public string Serial {get;init;}="ABC123456";
    public string PartNumber {get;init;}="";
    public string MacAddress {get;init;}="";
    public bool ShowPartNumber {get;init;}=true;
    public bool ShowMacAddress {get;init;}=true;
    public string LabelPresetId {get;init;}="";
    public string Language {get;init;}="en";
    public bool ShowBrand {get;init;}=true;
    public bool ShowModel {get;init;}=true;
    public bool ShowSerialText {get;init;}=true;
    public bool ShowCode {get;init;}=true;
    public bool ShowGuides {get;init;}=true;
    public bool AutoFont {get;init;}=true;
    public int FontPercent {get;init;}=100;
    public decimal MarginMm {get;init;}=2;
    public decimal OffsetXmm {get;init;}
    public decimal OffsetYmm {get;init;}
    public int Copies {get;init;}=1;
}
public static class WorkStore
{
    public static WorkState Load(string path)
    {
        string json=File.ReadAllText(path);
        var state=JsonSerializer.Deserialize<WorkState>(json)??new();
        using var doc=JsonDocument.Parse(json);
        // Preserve old printer/media preferences, adopt the requested new default layout.
        if(!doc.RootElement.TryGetProperty("Serial",out _))state=state with {Layout=3,Language="en"};
        if(!doc.RootElement.TryGetProperty("Brand",out _) && doc.RootElement.TryGetProperty("Title",out var legacyBrand))
            state=state with {Brand=legacyBrand.GetString()??""};
        if(!doc.RootElement.TryGetProperty("ShowBrand",out _) && doc.RootElement.TryGetProperty("ShowTitle",out var legacyVisibility))
            state=state with {ShowBrand=legacyVisibility.GetBoolean()};
        return state;
    }
    public static void Save(string path,WorkState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,path,true);
    }
}
