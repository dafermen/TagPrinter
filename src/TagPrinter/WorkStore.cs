using System.Text.Json;
namespace TagPrinter;
/// <summary>
/// Serializable snapshot of fields, visibility, printer and design. Missing JSON properties retain initial
/// values for compatibility.
/// </summary>
public record WorkState
{
    /// <summary>Installed printer name; history recovery requires availability before printing.</summary>
    public string Printer {get;init;}="";
    /// <summary>Legacy preset index; stable LabelPresetId is preferred.</summary>
    public int Size {get;init;}=0;
    /// <summary>Physical media width in millimeters.</summary>
    public decimal Width {get;init;}=100;
    /// <summary>Physical media height in millimeters.</summary>
    public decimal Height {get;init;}=50;
    /// <summary>0 bottom-center, 1 bottom-left, 2 bottom-right, 3 code beside text.</summary>
    public int Layout {get;init;}=3;
    /// <summary>Selector index: 0 Code 128, 1 QR.</summary>
    public int Code {get;init;}
    /// <summary>Selector index: 0 Windows driver, 1 direct RAW ZPL.</summary>
    public int Mode {get;init;}
    /// <summary>Saved selector index: 0 means 203 DPI, 1 means 300 DPI.</summary>
    public int Dpi {get;init;}
    /// <summary>Asset category text; absent older snapshots keep this empty.</summary>
    public string AssetType {get;init;}="";
    /// <summary>Whether this element is included in the label; its value is retained when hidden.</summary>
    public bool ShowAssetType {get;init;}=true;
    /// <summary>Manufacturer text; legacy Title migrates here when Brand is absent.</summary>
    public string Brand {get;init;}="Dell";
    /// <summary>Model description retained even when hidden.</summary>
    public string Model {get;init;}="22\" - H22203H";
    /// <summary>Symbol payload/asset identifier; may be blank for a code-free label.</summary>
    public string Serial {get;init;}="ABC123456";
    /// <summary>Optional part-number text.</summary>
    public string PartNumber {get;init;}="";
    /// <summary>Optional MAC text; not normalized or validated automatically.</summary>
    public string MacAddress {get;init;}="";
    /// <summary>Whether this element is included in the label; its value is retained when hidden.</summary>
    public bool ShowPartNumber {get;init;}=true;
    /// <summary>Whether this element is included in the label; its value is retained when hidden.</summary>
    public bool ShowMacAddress {get;init;}=true;
    /// <summary>Stable catalog key or custom for stored dimensions.</summary>
    public string LabelPresetId {get;init;}="";
    /// <summary>UI locale en/es; does not translate asset values.</summary>
    public string Language {get;init;}="en";
    /// <summary>Whether this element is included in the label; its value is retained when hidden.</summary>
    public bool ShowBrand {get;init;}=true;
    /// <summary>Whether this element is included in the label; its value is retained when hidden.</summary>
    public bool ShowModel {get;init;}=true;
    /// <summary>Whether this element is included in the label; its value is retained when hidden.</summary>
    public bool ShowSerialText {get;init;}=true;
    /// <summary>Whether this element is included in the label; its value is retained when hidden.</summary>
    public bool ShowCode {get;init;}=true;
    /// <summary>Preview-only margin guide, excluded from printed/exported output.</summary>
    public bool ShowGuides {get;init;}=true;
    /// <summary>Whether fitting may shrink text down to 50 percent.</summary>
    public bool AutoFont {get;init;}=true;
    /// <summary>Manual scale percentage from 50 through 200.</summary>
    public int FontPercent {get;init;}=100;
    /// <summary>Inner margin from 1 through 5 millimeters.</summary>
    public decimal MarginMm {get;init;}=2;
    /// <summary>Horizontal shift from -5 through 5 millimeters before rotation.</summary>
    public decimal OffsetXmm {get;init;}
    /// <summary>Vertical shift from -5 through 5 millimeters before rotation.</summary>
    public decimal OffsetYmm {get;init;}
    /// <summary>Windows font-family name; UI restoration falls back to Arial if absent.</summary>
    public string FontFamily {get;init;}="Arial";
    /// <summary>Body base size 4 through 72 points; heading uses 1.3 times this value.</summary>
    public decimal FontSizePt {get;init;}=10;
    /// <summary>Applies bold to every text line.</summary>
    public bool Bold {get;init;}
    /// <summary>Applies heading bold independently of general Bold.</summary>
    public bool HeadingBold {get;init;}=true;
    /// <summary>Clockwise degrees: 0, 90, 180 or 270.</summary>
    public int ContentRotation {get;init;}
    /// <summary>Centers the visible text block inside its allocated text area; false preserves legacy placement.</summary>
    public bool CenterText {get;init;}
    /// <summary>Symbol size percentage, 25 through 200; 100 preserves the original fitted size.</summary>
    public int CodePercent {get;init;}=100;
    /// <summary>Requested copies; a whole submission produces one history event.</summary>
    public int Copies {get;init;}=1;
}
/// <summary>Reads and atomically replaces last-work JSON, including legacy property migrations.</summary>
public static class WorkStore
{
    /// <summary>
    /// Deserializes work and migrates Title/ShowTitle to Brand/ShowBrand without replacing unrelated
    /// preferences.
    /// </summary>
    /// <param name="path">File path to read or write; test callers use their isolated verification directory.</param>
    /// <returns>Deserialized/migrated or validated state; file/JSON errors propagate.</returns>
    public static WorkState Load(string path)
    {
        string json=File.ReadAllText(path);
        var state=JsonSerializer.Deserialize<WorkState>(json)??new();
        // Inspect property presence separately from deserialization: missing legacy keys and explicitly empty new values are different cases.
        using var doc=JsonDocument.Parse(json);
        // Preserve old printer/media preferences, adopt the requested new default layout.
        if(!doc.RootElement.TryGetProperty("Serial",out _))state=state with {Layout=3,Language="en"};
        // Migrate only absent new properties. Explicit empty Brand values must not be replaced by legacy Title.
        if(!doc.RootElement.TryGetProperty("Brand",out _) && doc.RootElement.TryGetProperty("Title",out var legacyBrand))
            state=state with {Brand=legacyBrand.GetString()??""};
        if(!doc.RootElement.TryGetProperty("ShowBrand",out _) && doc.RootElement.TryGetProperty("ShowTitle",out var legacyVisibility))
            state=state with {ShowBrand=legacyVisibility.GetBoolean()};
        return state;
    }
    /// <summary>
    /// Writes a complete snapshot to a temporary sibling, then replaces the destination to reduce partially
    /// written files.
    /// </summary>
    /// <param name="path">File path to read or write; test callers use their isolated verification directory.</param>
    /// <param name="state">Complete saved-work snapshot.</param>
    public static void Save(string path,WorkState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // A sibling temporary file normally shares the destination volume. Replacement reduces partial
        // writes but does not provide multi-process locking.
        string temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,path,true);
    }
}
