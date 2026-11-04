using System.Text.Json;
namespace TagPrinter;

/// <summary>A named media preset. Id is a stable key; dimensions are physical millimeters.</summary>
/// <param name="Id">Stable preset key or numeric event ID, depending on this record.</param>
/// <param name="Name">Readable preset caption.</param>
/// <param name="WidthMm">Physical width in millimeters.</param>
/// <param name="HeightMm">Physical height in millimeters.</param>
public record LabelSize(string Id,string Name,decimal WidthMm,decimal HeightMm);
/// <summary>Initial preferences applied before restoring saved work; not a complete saved label.</summary>
public record ConfigDefaults
{
    /// <summary>UI locale en/es; does not translate asset values.</summary>
    public string Language {get;init;}="en";
    /// <summary>Stable catalog key or custom for stored dimensions.</summary>
    public string LabelPresetId {get;init;}="100x50";
    /// <summary>Actual resolution 203/300 pixels per inch; unlike WorkState.Dpi this is not an index.</summary>
    public int Dpi {get;init;}=203;
    /// <summary>0 bottom-center, 1 bottom-left, 2 bottom-right, 3 code beside text.</summary>
    public int Layout {get;init;}=3;
    /// <summary>Whether this element is included in the label; its value is retained when hidden.</summary>
    public bool ShowCode {get;init;}=true;
    /// <summary>True selects QR; false selects Code 128.</summary>
    public bool Qr {get;init;}
    /// <summary>Whether fitting may shrink text down to 50 percent.</summary>
    public bool AutoFont {get;init;}=true;
    /// <summary>Manual scale percentage from 50 through 200.</summary>
    public int FontPercent {get;init;}=100;
    /// <summary>Centers the visible text block inside its allocated text area; false preserves legacy placement.</summary>
    public bool CenterText {get;init;}
    /// <summary>Symbol size percentage, 25 through 200; 100 preserves the original fitted size.</summary>
    public int CodePercent {get;init;}=100;
    /// <summary>Inner margin from 1 through 5 millimeters.</summary>
    public decimal MarginMm {get;init;}=2;
}
/// <summary>
/// Loads and validates the installation-level label catalog and startup defaults. Saved work takes priority
/// over these defaults.
/// </summary>
public record AppConfig
{
    /// <summary>Installation config schema; only 1 is accepted.</summary>
    public int SchemaVersion {get;init;}=1;
    /// <summary>Media catalog with unique IDs; custom is reserved.</summary>
    public List<LabelSize> LabelSizes {get;init;}=new()
    {
        new("100x50","100 × 50 mm",100,50),new("50x25","50 × 25 mm",50,25),
        new("60x40","60 × 40 mm",60,40),new("80x50","80 × 50 mm",80,50),
        new("4x2","101.6 × 50.8 mm (4 × 2 in)",101.6m,50.8m),new("4x6","101.6 × 152.4 mm (4 × 6 in)",101.6m,152.4m)
    };
    /// <summary>Startup preferences applied before restoring saved work.</summary>
    public ConfigDefaults Defaults {get;init;}=new();
    /// <summary>Creates independent built-in catalog and startup preferences.</summary>
    /// <returns>Independent built-in configuration.</returns>
    public static AppConfig CreateDefault()=>new();
    /// <summary>
    /// Reads case-insensitive JSON and validates it. Missing files use built-in defaults; invalid files are
    /// not overwritten.
    /// </summary>
    /// <param name="path">File path to read or write; test callers use their isolated verification directory.</param>
    /// <returns>Deserialized/migrated or validated state; file/JSON errors propagate.</returns>
    public static AppConfig Load(string path)
    {
        if(!File.Exists(path))return CreateDefault();
        var config=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
            ??throw new InvalidDataException("config.json cannot be null.");
        config.Validate();return config;
    }
    /// <summary>
    /// Rejects unsupported schema versions, duplicate/reserved IDs, invalid dimensions and unselectable
    /// defaults.
    /// </summary>
    /// <exception cref="InvalidDataException">Configuration schema, catalog or defaults violate supported ranges.</exception>
    public void Validate()
    {
        if(SchemaVersion!=1)throw new InvalidDataException("config.json: SchemaVersion must be 1.");
        if(LabelSizes is null || LabelSizes.Count==0 || LabelSizes.Count>100)throw new InvalidDataException("config.json: provide between 1 and 100 label sizes.");
        // Case-sensitive stable IDs are independent of visible preset names; rejecting duplicates avoids ambiguous restoration.
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in LabelSizes)
        {
            if(item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id=="custom" || !ids.Add(item.Id) || string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException("config.json: label IDs must be unique and names cannot be empty. 'custom' is reserved.");
            if(item.WidthMm<25 || item.WidthMm>150 || item.HeightMm<20 || item.HeightMm>200 || decimal.Round(item.WidthMm,1)!=item.WidthMm || decimal.Round(item.HeightMm,1)!=item.HeightMm)
                throw new InvalidDataException("config.json: width 25–150 mm, height 20–200 mm, at most one decimal place.");
        }
        if(Defaults is null || Defaults.Language is not ("en" or "es") || Defaults.Dpi is not (203 or 300) || Defaults.Layout<0 || Defaults.Layout>3 || Defaults.CodePercent<25 || Defaults.CodePercent>200 || Defaults.FontPercent<50 || Defaults.FontPercent>200 || Defaults.MarginMm<1 || Defaults.MarginMm>5 || decimal.Round(Defaults.MarginMm,1)!=Defaults.MarginMm || !ids.Contains(Defaults.LabelPresetId))
            throw new InvalidDataException("config.json: invalid defaults. Check Language, LabelPresetId, Dpi, Layout, FontPercent and MarginMm.");
    }
}
