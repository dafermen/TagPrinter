using System.Text.Json;
namespace TagPrinter;

/// <summary>A versioned transfer envelope; history, binaries, fonts and Windows driver internals are not settings.</summary>
public sealed record ConfigurationBundle
{
    /// <summary>Identifies a full configuration export rather than installation config.json.</summary>
    public string Format {get;init;}="TagPrinter.Configuration";
    /// <summary>Transfer schema version, independent of AppConfig and SQLite versions.</summary>
    public int SchemaVersion {get;init;}=1;
    /// <summary>Exporting application version, for diagnostics.</summary>
    public string AppVersion {get;init;}="0.19.0";
    /// <summary>UTC export timestamp; not a label history event.</summary>
    public DateTimeOffset ExportedUtc {get;init;}=DateTimeOffset.UtcNow;
    /// <summary>Complete preset catalog and installation startup defaults.</summary>
    public AppConfig Catalog {get;init;}=new();
    /// <summary>All current editor values, visibility, format and application printer preferences.</summary>
    public WorkState Work {get;init;}=new();
}

/// <summary>Validates transfer files and stages paired writes with rollback for ordinary I/O failures.</summary>
public static class ConfigurationTransfer
{
    /// <summary>Maximum accepted/exported JSON size; matches the launcher's editable-config bound.</summary>
    public const int MaxBytes=1024*1024;

    /// <summary>Loads a recognizable complete envelope; invalid/partial/unknown-format input never mutates files.</summary>
    /// <param name="path">Export file selected by the user.</param>
    /// <returns>Validated catalog and editor snapshot.</returns>
    public static ConfigurationBundle Load(string path)
    {
        if(new FileInfo(path).Length>MaxBytes)throw new InvalidDataException("Configuration export exceeds 1 MB.");
        byte[] json=File.ReadAllBytes(path);using var document=JsonDocument.Parse(json);
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("Format",out var format) || format.ValueKind!=JsonValueKind.String || format.GetString()!="TagPrinter.Configuration" ||
            !root.TryGetProperty("SchemaVersion",out _) || !root.TryGetProperty("Catalog",out var catalog) || catalog.ValueKind!=JsonValueKind.Object ||
            !catalog.TryGetProperty("LabelSizes",out _) || !catalog.TryGetProperty("Defaults",out _) || !root.TryGetProperty("Work",out var work) || work.ValueKind!=JsonValueKind.Object)
            throw new InvalidDataException(Ui.T("Selecciona un archivo completo de Exportar configuración; config.json no es un respaldo completo.","Select a complete Export configuration file; config.json is not a full configuration backup."));
        foreach(string name in new[]{"Width","Height","Language","Printer","Layout","Code","Mode","Dpi","Copies","FontFamily","FontSizePt","LabelPresetId"})
            if(!work.TryGetProperty(name,out _))throw new InvalidDataException("Incomplete Work snapshot: "+name);
        var bundle=JsonSerializer.Deserialize<ConfigurationBundle>(json)??throw new InvalidDataException("Empty configuration.");
        Validate(bundle);return bundle;
    }

    /// <summary>Validates settings without requiring a printable label, allowing unfinished drafts to be transferred.</summary>
    /// <param name="bundle">Candidate to inspect before any writes or UI mutation.</param>
    public static void Validate(ConfigurationBundle bundle)
    {
        if(bundle.Format!="TagPrinter.Configuration" || bundle.SchemaVersion!=1 || bundle.Catalog is null || bundle.Work is null)throw new InvalidDataException("Unsupported configuration format/version.");
        bundle.Catalog.Validate();var s=bundle.Work;
        if(s.Width<20 || s.Width>200 || s.Height<20 || s.Height>200 || decimal.Round(s.Width,1)!=s.Width || decimal.Round(s.Height,1)!=s.Height ||
            s.FeedDirection<0 || s.FeedDirection>3 || s.Layout<0 || s.Layout>3 || s.Code<0 || s.Code>1 || s.Mode<0 || s.Mode>1 || s.Dpi<0 || s.Dpi>1 || s.Copies<1 || s.Copies>999 ||
            s.FontSizePt<4 || s.FontSizePt>72 || decimal.Round(s.FontSizePt)!=s.FontSizePt || s.FontPercent<50 || s.FontPercent>200 || s.CodePercent<25 || s.CodePercent>200 ||
            s.MarginMm<1 || s.MarginMm>5 || decimal.Round(s.MarginMm,1)!=s.MarginMm || Math.Abs(s.OffsetXmm)>5 || Math.Abs(s.OffsetYmm)>5 ||
            decimal.Round(s.OffsetXmm,1)!=s.OffsetXmm || decimal.Round(s.OffsetYmm,1)!=s.OffsetYmm || s.ContentRotation is not (0 or 90 or 180 or 270) || s.Language is not ("en" or "es"))
            throw new InvalidDataException(Ui.T("La configuración contiene valores fuera del rango permitido.","Configuration values are outside the supported ranges."));
        void Text(string? value,int max,string name){if(value is null || value.Length>max)throw new InvalidDataException("Invalid "+name);}
        Text(s.FreeText,2000,"FreeText");Text(s.AssetType,80,"AssetType");Text(s.Brand,80,"Brand");Text(s.Model,100,"Model");Text(s.Serial,120,"Serial");Text(s.PartNumber,100,"PartNumber");Text(s.MacAddress,100,"MacAddress");Text(s.Printer,512,"Printer");Text(s.FontFamily,256,"FontFamily");
        if(string.IsNullOrWhiteSpace(s.FontFamily) || string.IsNullOrEmpty(s.LabelPresetId))throw new InvalidDataException("Missing font or media selection.");
        if(s.LabelPresetId!="custom")
        {
            var preset=bundle.Catalog.LabelSizes.Find(p=>p.Id==s.LabelPresetId);
            if(preset is null || preset.WidthMm!=s.Width || preset.HeightMm!=s.Height)throw new InvalidDataException("The saved media does not match its catalog preset.");
        }
    }

    /// <summary>Writes a complete transfer file atomically, preserving an existing destination if serialization fails.</summary>
    /// <param name="path">Destination JSON, separate from live configuration and databases.</param>
    /// <param name="bundle">Validated application configuration and current work.</param>
    public static void Save(string path,ConfigurationBundle bundle)
    {
        Validate(bundle);byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(bundle,new JsonSerializerOptions{WriteIndented=true});
        if(bytes.Length>MaxBytes)throw new InvalidDataException("Configuration export exceeds 1 MB.");
        WriteAtomic(path,bytes);
    }

    /// <summary>Stages both files before replacing either, rolling back replacements on I/O failures. Not a power-loss transaction.</summary>
    /// <param name="bundle">Already validated settings, with unavailable local resources resolved by the UI.</param>
    /// <param name="catalogPath">Writable installation config.json destination.</param>
    /// <param name="workPath">Compatible last-work settings.json destination.</param>
    public static void Install(ConfigurationBundle bundle,string catalogPath,string workPath)
    {
        Validate(bundle);
        if(Path.GetFullPath(catalogPath).Equals(Path.GetFullPath(workPath),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Configuration destinations must differ.");
        byte[]? previousCatalog=File.Exists(catalogPath)?File.ReadAllBytes(catalogPath):null;
        byte[]? previousWork=File.Exists(workPath)?File.ReadAllBytes(workPath):null;
        string catalogTemp=catalogPath+"."+Guid.NewGuid()+".tmp",workTemp=workPath+"."+Guid.NewGuid()+".tmp";
        bool catalogChanged=false,workChanged=false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(workPath))!);
            byte[] catalog=JsonSerializer.SerializeToUtf8Bytes(bundle.Catalog,new JsonSerializerOptions{WriteIndented=true});
            if(catalog.Length>MaxBytes)throw new InvalidDataException("Catalog exceeds 1 MB.");
            File.WriteAllBytes(catalogTemp,catalog);
            File.WriteAllBytes(workTemp,JsonSerializer.SerializeToUtf8Bytes(bundle.Work,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(catalogTemp,catalogPath,true);catalogChanged=true;
            File.Move(workTemp,workPath,true);workChanged=true;
        }
        catch(Exception failure)
        {
            try
            {
                if(catalogChanged){if(previousCatalog is null)File.Delete(catalogPath);else WriteAtomic(catalogPath,previousCatalog);}
                if(workChanged){if(previousWork is null)File.Delete(workPath);else WriteAtomic(workPath,previousWork);}
            }
            catch(Exception rollback){throw new AggregateException("Import failed and rollback could not complete. Restore the before-import backup.",failure,rollback);}
            throw;
        }
        finally {if(File.Exists(catalogTemp))File.Delete(catalogTemp);if(File.Exists(workTemp))File.Delete(workTemp);}
    }

    /// <summary>Writes a unique sibling temporary file and replaces the target only after writing completes.</summary>
    private static void WriteAtomic(string path,byte[] bytes)
    {
        string temp=path+"."+Guid.NewGuid()+".tmp";
        try {File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
