using System.Text.Json;
namespace TagPrinter;

public record LabelSize(string Id,string Name,decimal WidthMm,decimal HeightMm);
public record ConfigDefaults
{
    public string Language {get;init;}="en";
    public string LabelPresetId {get;init;}="100x50";
    public int Dpi {get;init;}=203;
    public int Layout {get;init;}=3;
    public bool Qr {get;init;}
    public bool AutoFont {get;init;}=true;
    public int FontPercent {get;init;}=100;
    public decimal MarginMm {get;init;}=2;
}
public record AppConfig
{
    public int SchemaVersion {get;init;}=1;
    public List<LabelSize> LabelSizes {get;init;}=new()
    {
        new("100x50","100 × 50 mm",100,50),new("50x25","50 × 25 mm",50,25),
        new("60x40","60 × 40 mm",60,40),new("80x50","80 × 50 mm",80,50),
        new("4x2","101.6 × 50.8 mm (4 × 2 in)",101.6m,50.8m),new("4x6","101.6 × 152.4 mm (4 × 6 in)",101.6m,152.4m)
    };
    public ConfigDefaults Defaults {get;init;}=new();
    public static AppConfig CreateDefault()=>new();
    public static AppConfig Load(string path)
    {
        if(!File.Exists(path))return CreateDefault();
        var config=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
            ??throw new InvalidDataException("config.json cannot be null.");
        config.Validate();return config;
    }
    public void Validate()
    {
        if(SchemaVersion!=1)throw new InvalidDataException("config.json: SchemaVersion must be 1.");
        if(LabelSizes is null || LabelSizes.Count==0 || LabelSizes.Count>100)throw new InvalidDataException("config.json: provide between 1 and 100 label sizes.");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in LabelSizes)
        {
            if(item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id=="custom" || !ids.Add(item.Id) || string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException("config.json: label IDs must be unique and names cannot be empty. 'custom' is reserved.");
            if(item.WidthMm<25 || item.WidthMm>150 || item.HeightMm<20 || item.HeightMm>200 || decimal.Round(item.WidthMm,1)!=item.WidthMm || decimal.Round(item.HeightMm,1)!=item.HeightMm)
                throw new InvalidDataException("config.json: width 25–150 mm, height 20–200 mm, at most one decimal place.");
        }
        if(Defaults is null || Defaults.Language is not ("en" or "es") || Defaults.Dpi is not (203 or 300) || Defaults.Layout<0 || Defaults.Layout>3 || Defaults.FontPercent<50 || Defaults.FontPercent>200 || Defaults.MarginMm<1 || Defaults.MarginMm>5 || decimal.Round(Defaults.MarginMm,1)!=Defaults.MarginMm || !ids.Contains(Defaults.LabelPresetId))
            throw new InvalidDataException("config.json: invalid defaults. Check Language, LabelPresetId, Dpi, Layout, FontPercent and MarginMm.");
    }
}
