using System.Text.Json;
using ZXing;
namespace TagPrinter;

/// <summary>Exercises transfer and symbol scale against isolated files, actual forms and decoded pixels.</summary>
internal static class ConfigurationTests
{
    /// <summary>Checks full settings round trips, rejected/failed imports, backups and preserved history.</summary>
    internal static void Run(string folder)
    {
        string directory=Path.Combine(Path.GetTempPath(),"TagPrinter-configuration-tests",Guid.NewGuid().ToString());Directory.CreateDirectory(directory);
        var catalog=new AppConfig{LabelSizes=[new("wide","Wide test",100,50),new("small","Small test",60,40)],Defaults=new(){LabelPresetId="small",Language="es",Qr=true,Dpi=300,Layout=2,CodePercent=80}};
        string sourcePath=Path.Combine(directory,"source","settings.json");Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        string export=Path.Combine(directory,"transfer.json");ConfigurationBundle expected;
        using(var source=new MainForm(sourcePath,catalog))
        {
            source.Restore(new WorkState{LabelPresetId="wide",Width=100,Height=50,AssetType="Laptop",Brand="Dell",Model="5420",Serial="IMPORT123",PartNumber="PN9",MacAddress="00:11:22:33:44:55",Printer="Missing-transfer-printer",Code=1,Mode=1,Dpi=1,Copies=7,Language="es",ShowBrand=false,ShowMacAddress=false,ShowSerialText=false,ShowGuides=false,AutoFont=false,FontPercent=80,FontFamily="Segoe UI",FontSizePt=8,Bold=true,HeadingBold=false,MarginMm=3,OffsetXmm=.5m,OffsetYmm=-.5m,ContentRotation=180,CodePercent=80,CenterText=true});
            // Restore does not select unavailable printers, so export expectations use the actual captured UI.
            expected=source.CaptureConfiguration();source.ExportConfigurationFile(export);
            if(source.History.Query().Count!=0)throw new Exception("Configuration export created history");
        }
        var imported=ConfigurationTransfer.Load(export);
        if(imported.Work!=expected.Work || JsonSerializer.Serialize(imported.Catalog)!=JsonSerializer.Serialize(catalog))throw new Exception("Transfer lost configuration values");
        imported=imported with {Work=imported.Work with {Printer="Unavailable-transfer-printer"}};
        string targetPath=Path.Combine(directory,"target","settings.json");Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        WorkState resolved;
        using(var target=new MainForm(targetPath,AppConfig.CreateDefault()))
        {
            var before=target.CaptureConfiguration();string backup=target.ApplyConfiguration(imported);
            if(ConfigurationTransfer.Load(backup).Work!=before.Work)throw new Exception("Before-import backup lost live state");
            resolved=target.CaptureWork();
            if(resolved!=imported.Work with {Printer=""} || target.History.Query().Count!=0)throw new Exception("Imported UI/settings differ or created history");
            string installed=Path.Combine(Path.GetDirectoryName(targetPath)!,"catalog.json");
            if(JsonSerializer.Serialize(AppConfig.Load(installed))!=JsonSerializer.Serialize(catalog))throw new Exception("Catalog not installed");
            byte[] oldCatalog=File.ReadAllBytes(installed),oldWork=File.ReadAllBytes(targetPath);
            try{target.ExportConfigurationFile(installed);throw new Exception("Export overwrote live catalog");}catch(InvalidOperationException){}
            try{target.ApplyConfiguration(imported with {Work=imported.Work with {CodePercent=500}});throw new Exception("Invalid transfer accepted");}catch(InvalidDataException){}
            if(!File.ReadAllBytes(installed).SequenceEqual(oldCatalog) || !File.ReadAllBytes(targetPath).SequenceEqual(oldWork))throw new Exception("Rejected import changed files");
            // This preview shows the new controls, with no external data or printing involved.
            target.Show();target.assetSection.Expanded=false;target.templateSection.Expanded=true;target.settingsSection.Expanded=false;Application.DoEvents();
            using(var image=new Bitmap(target.Width,target.Height)){target.DrawToBitmap(image,target.ClientRectangle);image.Save(Path.Combine(folder,"configuration-transfer-es.png"));}
            target.assetSection.Expanded=true;target.templateSection.Expanded=false;target.settingsSection.Expanded=true;Application.DoEvents();
            using(var image=new Bitmap(target.Width,target.Height)){target.DrawToBitmap(image,target.ClientRectangle);image.Save(Path.Combine(folder,"configuration-settings-es.png"));}
            target.settingsSection.Expanded=false;
            target.ExportTagFile(Path.Combine(directory,"scaled-imported.png"));
            var generated=target.History.Query().Single();
            if(generated.State.CodePercent!=80)throw new Exception("History lost imported code size");
            target.Restore(target.CaptureWork() with {CodePercent=100});target.LoadHistoryEvent(generated);
            if(target.CaptureWork().CodePercent!=80)throw new Exception("History recovery lost code size");
            // Historical load deliberately switches to Custom; restore the imported preset for restart comparison.
            target.Restore(resolved);target.SaveConfig();
        }
        using(var reopened=new MainForm(targetPath))if(reopened.CaptureWork()!=resolved)throw new Exception("Imported settings/catalog did not survive restart: "+JsonSerializer.Serialize(reopened.CaptureWork())+" expected "+JsonSerializer.Serialize(resolved));
        string bad=Path.Combine(directory,"bad.json");
        foreach(string invalid in new[]{"{broken","{}",JsonSerializer.Serialize(imported with {SchemaVersion=99}),JsonSerializer.Serialize(imported with {Work=imported.Work with {Language="xx"}})})
        {
            File.WriteAllText(bad,invalid);
            try{ConfigurationTransfer.Load(bad);throw new Exception("Malformed/incomplete export accepted");}catch(Exception e) when(e is InvalidDataException or JsonException){}
        }
        string fontPath=Path.Combine(directory,"missing-font","settings.json");Directory.CreateDirectory(Path.GetDirectoryName(fontPath)!);
        using(var fontTarget=new MainForm(fontPath,AppConfig.CreateDefault()))
        {
            fontTarget.ApplyConfiguration(imported with {Work=imported.Work with {FontFamily="Missing transfer font 000"}});
            if(fontTarget.CaptureWork().FontFamily=="Missing transfer font 000")throw new Exception("Unavailable font did not fall back");
        }
        var draft=imported with {Work=imported.Work with {Serial="",ShowCode=true}};
        ConfigurationTransfer.Save(export,draft);if(ConfigurationTransfer.Load(export).Work.Serial!="")throw new Exception("Draft transfer lost empty serial");
        string rollbackCatalog=Path.Combine(directory,"rollback.json"),blockedWork=Path.Combine(directory,"blocked-work");
        File.WriteAllText(rollbackCatalog,"original catalog bytes");Directory.CreateDirectory(blockedWork);
        try{ConfigurationTransfer.Install(imported,rollbackCatalog,blockedWork);throw new Exception("Blocked destination accepted");}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
        if(File.ReadAllText(rollbackCatalog)!="original catalog bytes")throw new Exception("Paired-write rollback failed");
        TestCodeScale(folder);
    }

    /// <summary>Decodes resized labels and standalone images at both supported DPIs; tests overflow and history defaults.</summary>
    private static void TestCodeScale(string folder)
    {
        foreach(int dpi in new[]{203,300})foreach(bool qr in new[]{false,true})foreach(int percent in new[]{50,100,140})
        {
            var data=new LabelData("Dell","M","SCALE123",100,50,dpi,qr?0:3,qr,ShowSerialText:false,CodePercent:percent);
            using var label=LabelRenderer.Render(data);Decode(label,"SCALE123");
            using var symbol=CodeRenderer.Render("SCALE123",qr,dpi,percent);Decode(symbol,"SCALE123");
            if(percent==50){using var original=CodeRenderer.Render("SCALE123",qr,dpi);if(symbol.Width>=original.Width || symbol.Height>=original.Height)throw new Exception("Standalone size did not decrease");}
            if(dpi==203)label.Save(Path.Combine(folder,$"code-size-{qr}-{percent}.png"));
        }
        using(var rotated=LabelRenderer.Render(new LabelData("Dell","M","ROTATED",100,50,203,0,true,ShowSerialText:false,ContentRotation:90,CodePercent:75)))Decode(rotated,"ROTATED");
        try{using var tooLarge=LabelRenderer.Render(new LabelData("Dell","M","OVERFLOW",50,25,203,3,true,CodePercent:200));throw new Exception("Oversized code silently clipped");}catch(ArgumentException){}
        if(JsonSerializer.Deserialize<WorkState>("{}")!.CodePercent!=100 || JsonSerializer.Deserialize<ConfigDefaults>("{}")!.CodePercent!=100)throw new Exception("Older settings changed symbol size");
    }

    /// <summary>Converts actual bitmap pixels to packed RGB and verifies the generated symbol's exact serial.</summary>
    private static void Decode(Bitmap image,string serial)
    {
        var rgb=new byte[image.Width*image.Height*3];
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var pixel=image.GetPixel(x,y);int index=(y*image.Width+x)*3;rgb[index]=pixel.R;rgb[index+1]=pixel.G;rgb[index+2]=pixel.B;}
        var decoded=new BarcodeReaderGeneric{AutoRotate=true,Options=new ZXing.Common.DecodingOptions{TryHarder=true}}.Decode(new RGBLuminanceSource(rgb,image.Width,image.Height,RGBLuminanceSource.BitmapFormat.RGB24));
        if(decoded?.Text!=serial)throw new Exception("Resized code could not be decoded");
    }
}
