using ZXing;
namespace TagPrinter;
public static class SelfTest
{
    public static void Run()
    {
        string folder=Path.Combine(AppContext.BaseDirectory,"verification");Directory.CreateDirectory(folder);
        int count=0;
        foreach(int width in new[]{50,100}) foreach(int dpi in new[]{203,300}) foreach(int layout in new[]{0,1,2,3}) foreach(bool qr in new[]{false,true})
        {
            var data=new LabelData("Monitor","Dell 22\" - H22203H","ABC12345644",width,width/2,dpi,layout,qr);
            using var bmp=LabelRenderer.Render(data);
            var rgb=new byte[bmp.Width*bmp.Height*3];
            for(int y=0;y<bmp.Height;y++)for(int x=0;x<bmp.Width;x++){var c=bmp.GetPixel(x,y);int i=(y*bmp.Width+x)*3;rgb[i]=c.R;rgb[i+1]=c.G;rgb[i+2]=c.B;}
            var reader=new BarcodeReaderGeneric {AutoRotate=true,Options=new ZXing.Common.DecodingOptions{TryHarder=true}};
            var decoded=reader.Decode(new RGBLuminanceSource(rgb,bmp.Width,bmp.Height,RGBLuminanceSource.BitmapFormat.RGB24));
            if(decoded?.Text!=data.Serial)throw new Exception($"No se pudo leer el código: {dpi}, {layout}, QR={qr}");
            string zpl=LabelRenderer.ToZpl(bmp,2);
            if(!zpl.StartsWith($"^XA^PW{bmp.Width}^LL{bmp.Height}")||!zpl.EndsWith("^PQ2^XZ"))throw new Exception("ZPL incorrecto");
            bmp.Save(Path.Combine(folder,$"label-{width}-{dpi}-{layout}-{qr}.png"));count++;
        }
        try {using var invalid=LabelRenderer.Render(new LabelData("Monitor","Dell","",100,50,203,0,false));throw new Exception("Se aceptó un serial vacío");}catch(ArgumentException){}
        TestTypographyAndMargins(folder);
        TestCodeExport(folder);
        TestFieldVisibility(folder);
        TestConfigurationAndAssetFields(folder);
        TestAssetType(folder);
        TestHistory(folder);
        string statePath=Path.Combine(folder,"test-work.json");
        var work=new WorkState{AssetType="Laptop",ShowAssetType=false,Brand="Laptop",Model="Dell Latitude",Serial="TEST/123",Size=6,Width=65,Height=35,Layout=3,Code=1,Dpi=1,Copies=4,Language="es",AutoFont=false,FontPercent=70,MarginMm=3,OffsetXmm=.5m,OffsetYmm=-.5m,ShowBrand=false,ShowModel=true,ShowSerialText=false,PartNumber="PN-DEMO-001",MacAddress="00:11:22:33:44:55",ShowPartNumber=false,ShowMacAddress=false};
        WorkStore.Save(statePath,work);
        if(WorkStore.Load(statePath)!=work)throw new Exception("Work persistence mismatch");
        if(MainForm.SafeFileName("ABC12345644")!="ABC12345644" || MainForm.SafeFileName("TEST/123")!="TEST_123" || MainForm.SafeFileName("CON")!="_CON")throw new Exception("Invalid file name");
        string freshPath=Path.Combine(folder,"fresh-"+Guid.NewGuid()+".json");
        using(var form=new MainForm(freshPath,AppConfig.CreateDefault()))
        {
            if(!form.CaptureWork().ShowBrand || !form.CaptureWork().ShowModel || !form.CaptureWork().ShowSerialText || form.CaptureWork().Layout!=3 || form.CaptureWork().Language!="en")throw new Exception("Defaults mismatch");
            form.Show();Application.DoEvents();
            if(form.templateSection.Expanded || form.textSection.Expanded || form.printingSection.Expanded || form.settingsSection.Expanded)throw new Exception("Sections must start collapsed");
            if(!form.assetSection.Expanded || form.AboutTooltip!="About")throw new Exception("Asset section or About tooltip default incorrect");
            var before=form.CaptureWork();
            form.assetSection.Toggle();Application.DoEvents();
            if(form.assetSection.Expanded || form.assetSection.Body.Visible || form.CaptureWork()!=before)throw new Exception("Asset details cannot collapse safely");
            form.assetSection.Toggle();
            form.templateSection.Toggle();form.textSection.Toggle();form.printingSection.Toggle();form.settingsSection.Toggle();Application.DoEvents();
            if(!form.templateSection.Expanded || !form.textSection.Expanded || !form.printingSection.Expanded || !form.settingsSection.Expanded || !form.templateSection.Body.Visible || !form.textSection.Body.Visible || !form.printingSection.Body.Visible || !form.settingsSection.Body.Visible)throw new Exception("Sections did not expand");
            form.templateSection.Toggle();form.textSection.Toggle();form.printingSection.Toggle();form.settingsSection.Toggle();Application.DoEvents();
            if(form.templateSection.Expanded || form.textSection.Expanded || form.printingSection.Expanded || form.settingsSection.Expanded || form.CaptureWork()!=before)throw new Exception("Collapsing changed the work");
            form.ChangeFont(-10);
            if(form.CaptureWork().AutoFont || form.CaptureWork().FontPercent!=90)throw new Exception("Font buttons failed");
            form.Restore(new WorkState());
            using(var screenshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(screenshot,new Rectangle(Point.Empty,form.Size));screenshot.Save(Path.Combine(folder,"application-en.png"));}
            form.Restore(work with {Brand="Monitor",Model="Dell 22\" - H22203H",Serial="ABC12345644",Size=1,Width=50,Height=25,Code=0,Dpi=0,AutoFont=true,MarginMm=2,OffsetXmm=0,OffsetYmm=0});
            Application.DoEvents();
            using(var screenshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(screenshot,new Rectangle(Point.Empty,form.Size));screenshot.Save(Path.Combine(folder,"application-es-small.png"));}
            form.Restore(work with {Serial="ABC12345644",Copies=7,Mode=1});
            if(!form.PrintSummaryText.Contains("7") || !form.PrintSummaryText.Contains("ZPL"))throw new Exception("Print summary is stale");
            var full=form.CaptureWork();form.StartNewAsset();
            var cleared=form.CaptureWork();
            if(cleared!=full with {AssetType="",Brand="",Model="",Serial="",PartNumber="",MacAddress=""})throw new Exception("New asset changed preferences or retained old data");
            if(WorkStore.Load(freshPath)!=cleared)throw new Exception("New asset was not saved immediately");
            if(!form.PrintSummaryText.Contains("7") || !form.PrintSummaryText.Contains("ZPL"))throw new Exception("New asset changed printing summary");
            if(new Uri(MainForm.ProjectUrl).AbsoluteUri!="https://github.com/dafermen/TagPrinter")throw new Exception("Incorrect project link");
            form.Restore(work with {Serial="ABC12345644"});
            form.SaveConfig();form.Close();
        }
        using(var reopened=new MainForm(freshPath,AppConfig.CreateDefault()))
        {
            if(reopened.templateSection.Expanded || reopened.textSection.Expanded || reopened.printingSection.Expanded || reopened.settingsSection.Expanded)throw new Exception("Reopened sections must start collapsed");
            var restored=reopened.CaptureWork();
            if(restored.Serial!="ABC12345644" || restored.Language!="es" || restored.Layout!=3 || restored.Copies!=4 || restored.AutoFont || restored.FontPercent!=70 || restored.MarginMm!=3 || restored.OffsetXmm!=.5m || restored.OffsetYmm!=-.5m || restored.ShowBrand || !restored.ShowModel || restored.ShowSerialText || restored.PartNumber!="PN-DEMO-001" || restored.MacAddress!="00:11:22:33:44:55" || restored.ShowPartNumber || restored.ShowMacAddress || restored.AssetType!="Laptop" || restored.ShowAssetType)throw new Exception("UI restore failed");
        }
        File.WriteAllText(Path.Combine(folder,"results.txt"),$"PASS: {count} etiquetas generadas y decodificadas (2 tamaños × 2 resoluciones × 4 plantillas × 2 tipos). Serial vacío rechazado. Persistencia de trabajo/idioma, reapertura y nombres de archivo comprobados. Fuente manual/automática, márgenes, guías y desplazamientos comprobados. ZPL con dimensiones y cantidad comprobados. Exportación independiente QR/Code 128 y nombres de archivo comprobados. Visibilidad de campos: 32 combinaciones decodificadas y preferencias restauradas. Campos P/N y MAC, configuración JSON y migración Title/Brand comprobados. Tipo de activo, seis campos, visibilidad y persistencia comprobados. Nuevo activo y resumen de impresión comprobados. SQLite, búsqueda, filtros, CSV, backup, exportaciones y recuperación histórica comprobados. No se envió ningún trabajo a impresoras.");
    }
    static void TestTypographyAndMargins(string folder)
    {
        var data=new LabelData("Monitor","Dell 22\" - H22203H","ABC12345644",100,50,203,3,false);
        using var smaller=LabelRenderer.Render(data with {AutoFont=false,FontPercent=60});
        using var larger=LabelRenderer.Render(data with {AutoFont=false,FontPercent=80});
        int textChanges=0;
        for(int y=0;y<smaller.Height;y++)for(int x=0;x<smaller.Width;x++)
        {
            if(smaller.GetPixel(x,y).ToArgb()!=larger.GetPixel(x,y).ToArgb())
            {
                if(x>smaller.Width/2)throw new Exception("Text size modified the code");
                textChanges++;
            }
        }
        if(textChanges==0)throw new Exception("Font control had no effect");
        foreach(int dpi in new[]{203,300})foreach(decimal margin in new[]{1m,2m,5m})foreach(bool qr in new[]{false,true})
        {
            var settings=data with {Dpi=dpi,MarginMm=margin,Qr=qr};
            using var image=LabelRenderer.Render(settings);
            int m=(int)Math.Round((double)margin*dpi/25.4);
            var rgb=new byte[image.Width*image.Height*3];
            for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
            {
                var c=image.GetPixel(x,y);int i=(y*image.Width+x)*3;rgb[i]=c.R;rgb[i+1]=c.G;rgb[i+2]=c.B;
                if((x<m || y<m || x>=image.Width-m || y>=image.Height-m)&&c.ToArgb()!=Color.White.ToArgb())throw new Exception("Margin contains ink");
            }
            var decoded=new BarcodeReaderGeneric{Options=new ZXing.Common.DecodingOptions{TryHarder=true}}.Decode(new RGBLuminanceSource(rgb,image.Width,image.Height,RGBLuminanceSource.BitmapFormat.RGB24));
            if(decoded?.Text!=data.Serial)throw new Exception("Code unreadable with margin");
        }
        using var original=LabelRenderer.Render(data);
        using var guided=LabelRenderer.Render(data,true);
        using var clean=LabelRenderer.Render(data);
        bool guideFound=false;
        for(int y=0;y<original.Height;y++)for(int x=0;x<original.Width;x++)
        {
            if(original.GetPixel(x,y)!=clean.GetPixel(x,y))throw new Exception("Guide leaked into output");
            if(guided.GetPixel(x,y)!=original.GetPixel(x,y))guideFound=true;
        }
        if(!guideFound)throw new Exception("No preview guide");
        foreach(int direction in new[]{-1,1})
        {
            using var moved=LabelRenderer.Render(data with {OffsetXmm=direction,OffsetYmm=direction});
            int delta=(int)Math.Round(direction*203/25.4);
            for(int y=0;y<original.Height;y++)for(int x=0;x<original.Width;x++)
            {
                int sx=x-delta,sy=y-delta;
                var expected=sx>=0&&sy>=0&&sx<original.Width&&sy<original.Height?original.GetPixel(sx,sy):Color.White;
                if(moved.GetPixel(x,y).ToArgb()!=expected.ToArgb())throw new Exception("Offset did not translate exactly");
            }
        }
        void MustReject(LabelData invalid)
        {
            try{using var image=LabelRenderer.Render(invalid);}catch(ArgumentException){return;}
            throw new Exception("Unsafe settings accepted");
        }
        MustReject(data with {WidthMm=50,HeightMm=25,AutoFont=false,FontPercent=200});
        MustReject(data with {OffsetXmm=3});
        MustReject(data with {MarginMm=0});
        string legacy=Path.Combine(folder,"v02-work.json");File.WriteAllText(legacy,"{\"Serial\":\"OLD123\"}");
        var migrated=WorkStore.Load(legacy);
        if(migrated.MarginMm!=2 || !migrated.AutoFont || migrated.FontPercent!=100 || migrated.OffsetXmm!=0)throw new Exception("Migration failed");
        guided.Save(Path.Combine(folder,"margin-guide-preview.png"));
        original.Save(Path.Combine(folder,"margin-clean-export.png"));
    }

    static void TestCodeExport(string folder)
    {
        foreach(int dpi in new[]{203,300})foreach(bool qr in new[]{false,true})
        {
            string serial="ABC12345644";
            using var image=CodeRenderer.Render(serial,qr,dpi);
            string path=Path.Combine(folder,MainForm.CodeFileName(serial,qr).Replace(".png",$"-{dpi}.png"));
            image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
            using var loaded=new Bitmap(path);
            var bytes=new byte[loaded.Width*loaded.Height*3];
            int module=Math.Max(4,(int)Math.Round(dpi/50.8));
            int qx=(qr?4:10)*module,qy=4*module;
            for(int y=0;y<loaded.Height;y++)for(int x=0;x<loaded.Width;x++)
            {
                var pixel=loaded.GetPixel(x,y);int i=(y*loaded.Width+x)*3;
                if(pixel.A!=255 || (pixel.ToArgb()!=Color.White.ToArgb() && pixel.ToArgb()!=Color.Black.ToArgb()))throw new Exception("Code PNG must be opaque black/white");
                if((x<qx || x>=loaded.Width-qx || y<qy || y>=loaded.Height-qy) && pixel.ToArgb()!=Color.White.ToArgb())throw new Exception("Code quiet zone is not white");
                bytes[i]=pixel.R;bytes[i+1]=pixel.G;bytes[i+2]=pixel.B;
            }
            if(qr && loaded.Width!=loaded.Height)throw new Exception("QR must be square");
            if(Math.Abs(loaded.HorizontalResolution-dpi)>.1)throw new Exception("PNG DPI mismatch");
            var result=new BarcodeReaderGeneric{Options=new ZXing.Common.DecodingOptions{TryHarder=true}}
                .Decode(new RGBLuminanceSource(bytes,loaded.Width,loaded.Height,RGBLuminanceSource.BitmapFormat.RGB24));
            if(result?.Text!=serial || result.BarcodeFormat!=(qr?BarcodeFormat.QR_CODE:BarcodeFormat.CODE_128))throw new Exception("Exported PNG could not be decoded");
        }
        if(MainForm.CodeFileName("A/B",true)!="A_B_QR.png" || MainForm.CodeFileName("ABC123",false)!="ABC123_barcode.png")throw new Exception("Code filename mismatch");
        foreach(bool qr in new[]{false,true})
        {
            try{using var invalid=CodeRenderer.Render("",qr,203);throw new Exception("Blank serial accepted");}catch(ArgumentException){}
        }
        try{using var invalid=CodeRenderer.Render("Ñ123",false,203);throw new Exception("Non-ASCII barcode accepted");}catch(ArgumentException){}
        using var unicode=CodeRenderer.Render("Ñ123",true,203);
        // Code rendering is independent of title, template, offsets and font fitting.
        using var standalone=CodeRenderer.Render(new string('A',80),false,203);
    }

    static void TestFieldVisibility(string folder)
    {
        foreach(int layout in new[]{0,3})foreach(bool qr in new[]{false,true})foreach(int mask in Enumerable.Range(0,8))
        {
            var data=new LabelData("Monitor","Dell 22\" - H22203H","ABC12345644",100,50,203,layout,qr,ShowBrand:(mask&1)!=0,ShowModel:(mask&2)!=0,ShowSerialText:(mask&4)!=0);
            using var image=LabelRenderer.Render(data);
            var rgb=new byte[image.Width*image.Height*3];
            for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
            {
                var pixel=image.GetPixel(x,y);int i=(y*image.Width+x)*3;rgb[i]=pixel.R;rgb[i+1]=pixel.G;rgb[i+2]=pixel.B;
            }
            var result=new BarcodeReaderGeneric{Options=new ZXing.Common.DecodingOptions{TryHarder=true}}.Decode(new RGBLuminanceSource(rgb,image.Width,image.Height,RGBLuminanceSource.BitmapFormat.RGB24));
            if(result?.Text!=data.Serial)throw new Exception("Hidden text changed the code payload");
            if(!data.ShowBrand || !data.ShowModel)
            {
                using var hidden=LabelRenderer.Render(data with {Brand=data.ShowBrand?data.Brand:new string('X',300),Model=data.ShowModel?data.Model:new string('X',300)});
                for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(image.GetPixel(x,y)!=hidden.GetPixel(x,y))throw new Exception("Hidden field rendered text");
            }
            if(mask==0)image.Save(Path.Combine(folder,$"label-code-only-{layout}-{qr}.png"));
        }
        string legacy=Path.Combine(folder,"visibility-legacy.json");File.WriteAllText(legacy,"{\"Serial\":\"LEGACY123\"}");
        var migrated=WorkStore.Load(legacy);
        if(!migrated.ShowBrand || !migrated.ShowModel || !migrated.ShowSerialText)throw new Exception("Existing work lost visible fields");
        try{using var invalid=LabelRenderer.Render(new LabelData("","","",100,50,203,3,true,ShowBrand:false,ShowModel:false,ShowSerialText:false));throw new Exception("Hidden serial allowed empty code payload");}catch(ArgumentException){}
    }

    static void TestConfigurationAndAssetFields(string folder)
    {
        var options=new System.Text.Json.JsonSerializerOptions{WriteIndented=true};
        string configFile=Path.Combine(folder,"test-config.json");
        var custom=new AppConfig{LabelSizes=new(){new("demo","75 × 35 mm",75,35)},Defaults=new(){LabelPresetId="demo",Dpi=300,Language="es",Layout=2,MarginMm=3,AutoFont=false,FontPercent=80}};
        File.WriteAllText(configFile,System.Text.Json.JsonSerializer.Serialize(custom,options));
        var loaded=AppConfig.Load(configFile);
        string workFile=Path.Combine(folder,"config-work-"+Guid.NewGuid()+".json");
        using(var form=new MainForm(workFile,loaded))
        {
            var work=form.CaptureWork();
            if(work.Width!=75 || work.Height!=35 || work.Language!="es" || work.Dpi!=1 || work.Layout!=2 || work.MarginMm!=3 || work.AutoFont || work.FontPercent!=80)throw new Exception("Config defaults not applied");
            form.Restore(work with {Brand="Dell",Model="Latitude 5420",PartNumber="PN-DEMO-001",MacAddress="00:11:22:33:44:55",ShowPartNumber=false,ShowMacAddress=true});form.SaveConfig();
        }
        using(var restored=new MainForm(workFile,AppConfig.CreateDefault()))
        {
            var work=restored.CaptureWork();
            if(work.PartNumber!="PN-DEMO-001" || work.MacAddress!="00:11:22:33:44:55" || work.ShowPartNumber || !work.ShowMacAddress || work.Width!=75 || work.Height!=35 || work.LabelPresetId!="custom")throw new Exception("Asset fields or removed preset not restored");
        }
        var reversed=AppConfig.CreateDefault() with {LabelSizes=AppConfig.CreateDefault().LabelSizes.AsEnumerable().Reverse().ToList()};
        WorkStore.Save(workFile,new WorkState{LabelPresetId="50x25",Width=50,Height=25});
        using(var form=new MainForm(workFile,reversed))if(form.CaptureWork().LabelPresetId!="50x25" || form.CaptureWork().Width!=50)throw new Exception("Preset ID did not survive reordering");
        foreach(var invalid in new[]{custom with {SchemaVersion=99},custom with {LabelSizes=new()},custom with {LabelSizes=new(){new("demo","Bad",3,5)}},custom with {Defaults=custom.Defaults with {Dpi=600}},custom with {Defaults=custom.Defaults with {LabelPresetId="missing"}}})
        {
            try{invalid.Validate();throw new Exception("Invalid config accepted");}catch(InvalidDataException){}
        }
        File.WriteAllText(configFile,"{");
        try{AppConfig.Load(configFile);throw new Exception("Malformed JSON accepted");}catch(System.Text.Json.JsonException){}
        string legacy=Path.Combine(folder,"legacy-title.json");File.WriteAllText(legacy,"{\"Title\":\"Legacy brand\",\"ShowTitle\":false,\"Serial\":\"OLD123\"}");
        var migrated=WorkStore.Load(legacy);
        if(migrated.Brand!="Legacy brand" || migrated.ShowBrand || migrated.PartNumber!="" || migrated.MacAddress!="")throw new Exception("Title migration failed");
        foreach(bool qr in new[]{false,true})foreach(int mask in Enumerable.Range(0,4))
        {
            var data=new LabelData("Dell","Latitude 5420","DEMO123456",100,50,203,3,qr,PartNumber:"PN-DEMO-001",MacAddress:"00:11:22:33:44:55",ShowPartNumber:(mask&1)!=0,ShowMacAddress:(mask&2)!=0);
            using var image=LabelRenderer.Render(data);
            var rgb=new byte[image.Width*image.Height*3];
            for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var c=image.GetPixel(x,y);int i=(y*image.Width+x)*3;rgb[i]=c.R;rgb[i+1]=c.G;rgb[i+2]=c.B;}
            var result=new BarcodeReaderGeneric{Options=new ZXing.Common.DecodingOptions{TryHarder=true}}.Decode(new RGBLuminanceSource(rgb,image.Width,image.Height,RGBLuminanceSource.BitmapFormat.RGB24));
            if(result?.Text!=data.Serial)throw new Exception("New asset fields changed code payload");
        }
        using(var screenshotForm=new MainForm(Path.Combine(folder,"screenshot-"+Guid.NewGuid()+".json"),AppConfig.CreateDefault()))
        {
            screenshotForm.Restore(new WorkState{AssetType="Monitor",Brand="Dell",Model="H22203H",Serial="ABC123",PartNumber="ZYX123",MacAddress="00:1A:2B:3C:4D:5E",Code=1});
            screenshotForm.Show();Application.DoEvents();
            using(var capture=new Bitmap(screenshotForm.Width,screenshotForm.Height)){screenshotForm.DrawToBitmap(capture,new Rectangle(Point.Empty,screenshotForm.Size));capture.Save(Path.Combine(folder,"asset-fields-en.png"));}
            screenshotForm.Restore(screenshotForm.CaptureWork() with {Language="es"});Application.DoEvents();
            using(var capture=new Bitmap(screenshotForm.Width,screenshotForm.Height)){screenshotForm.DrawToBitmap(capture,new Rectangle(Point.Empty,screenshotForm.Size));capture.Save(Path.Combine(folder,"asset-fields-es.png"));}
            screenshotForm.Close();
        }
    }

    static void TestAssetType(string folder)
    {
        foreach(int dpi in new[]{203,300})foreach(bool qr in new[]{false,true})foreach(bool visible in new[]{false,true})
        {
            var data=new LabelData("Dell","H22203H","ABC123",100,50,dpi,3,qr,PartNumber:"ZYX123",MacAddress:"00:1A:2B:3C:4D:5E",AssetType:"Monitor",ShowAssetType:visible);
            using var image=LabelRenderer.Render(data);
            var rgb=new byte[image.Width*image.Height*3];
            for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var c=image.GetPixel(x,y);int i=(y*image.Width+x)*3;rgb[i]=c.R;rgb[i+1]=c.G;rgb[i+2]=c.B;}
            var decoded=new BarcodeReaderGeneric{Options=new ZXing.Common.DecodingOptions{TryHarder=true}}.Decode(new RGBLuminanceSource(rgb,image.Width,image.Height,RGBLuminanceSource.BitmapFormat.RGB24));
            if(decoded?.Text!="ABC123")throw new Exception("Asset type changed code payload");
            if(!visible)
            {
                using var changed=LabelRenderer.Render(data with {AssetType=new string('X',300)});
                for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(image.GetPixel(x,y)!=changed.GetPixel(x,y))throw new Exception("Hidden asset type rendered");
            }
            else if(dpi==203)image.Save(Path.Combine(folder,qr?"six-fields-qr.png":"six-fields-barcode.png"));
        }
        string legacy=Path.Combine(folder,"asset-type-legacy.json");File.WriteAllText(legacy,"{\"Brand\":\"Dell\",\"Serial\":\"OLD123\"}");
        if(WorkStore.Load(legacy).AssetType!="")throw new Exception("Invented asset type for existing work");
    }

    static void TestHistory(string folder)
    {
        string testFolder=Path.Combine(folder,"history-"+Guid.NewGuid());Directory.CreateDirectory(testFolder);
        var store=new HistoryStore(Path.Combine(testFolder,"TagPrinter.db"));
        var state=new WorkState{AssetType="Monitor",Brand="Dell",Model="Text; \"quote\"\r\nsecond line",Serial="000123",PartNumber="PN-01",MacAddress="00:1A:2B:3C:4D:5E",Width=100,Height=50,MarginMm=3,OffsetXmm=.5m,ShowMacAddress=false,Copies=2,Code=1,Printer="Missing-Test-Printer",LabelPresetId="100x50"};
        var firstTime=DateTimeOffset.Parse("2026-10-31T23:59:59Z");
        long first=store.Add("ExportTag",state,"test.png",timestamp:firstTime);
        long second=store.Add("PrintSubmitted",state,sourceEventId:first,timestamp:firstTime.AddSeconds(1));
        if(first==second || store.Query().Count!=2 || store.Get(first)?.State!=state || store.Get(second)?.SourceEventId!=first)throw new Exception("History append/snapshot failed");
        if(store.Query(new("000123","serial")).Count!=2 || store.Query(new("Dell","brand")).Count!=2 || store.Query(new("Dell","model")).Count!=0)throw new Exception("History field search failed");
        if(store.Query(new(Action:"PrintSubmitted")).Count!=1 || store.Query(new(FromUtc:firstTime,ToUtcExclusive:firstTime.AddSeconds(1))).Count!=1)throw new Exception("History action/date filter failed");
        store.Add("ExportCode",state with {Serial="A_B%123"});
        if(store.Query(new("A_B%","serial")).Count!=1 || store.Query(new("%' OR 1=1--")).Count!=0)throw new Exception("Search escaping failed");
        string csv=Path.Combine(testFolder,"roundtrip.csv");HistoryStore.ExportCsv(csv,store.Query());
        using(var parser=new Microsoft.VisualBasic.FileIO.TextFieldParser(csv,System.Text.Encoding.UTF8))
        {
            parser.SetDelimiters(";");parser.HasFieldsEnclosedInQuotes=true;
            var header=parser.ReadFields()!;var fields=parser.ReadFields()!;
            if(header.Length!=17 || fields[3]!="A_B%123" || fields[6]!=state.Model || System.Text.Json.JsonSerializer.Deserialize<WorkState>(fields[16])!=state with {Serial="A_B%123"})throw new Exception("CSV quoting/state roundtrip failed");
        }
        string backup=Path.Combine(testFolder,"backup.db");store.Backup(backup);
        if(new HistoryStore(backup).Query().Count!=3)throw new Exception("SQLite backup did not preserve history");
        try{store.Backup(store.DatabasePath);throw new Exception("Backup overwrote live database");}catch(ArgumentException){}
        if(new HistoryStore(store.DatabasePath).Query().Count!=3)throw new Exception("History was not durable");
        var config=AppConfig.CreateDefault() with {LabelSizes=AppConfig.CreateDefault().LabelSizes.Select(p=>p.Id=="100x50"?p with {WidthMm=120,HeightMm=70}:p).ToList()};
        using(var form=new MainForm(Path.Combine(testFolder,"work.json"),config,store.DatabasePath))
        {
            form.Show();Application.DoEvents();form.ShowReports();
            if(form.Reports.VisibleRows().Count!=3)throw new Exception("Reports starts with unintended date filter");
            form.Reports.Grid.Sort(form.Reports.Grid.Columns["Id"],System.ComponentModel.ListSortDirection.Ascending);
            if(form.Reports.VisibleRows()[0].Id!=first)throw new Exception("Report sorting/export order failed");
            int before=store.Query().Count;form.Reports.OpenRecordForTest(first);
            var restored=form.CaptureWork();
            if(!form.ShowingLabel || restored.Width!=100 || restored.Height!=50 || restored.LabelPresetId!="custom" || restored.Model!=state.Model || restored.MarginMm!=3 || restored.ShowMacAddress || restored.Printer!="" || store.Query().Count!=before)throw new Exception("Historical recovery changed design or printed automatically");
            form.ExportTagFile(Path.Combine(testFolder,"actual-tag.png"));
            var tag=store.Query()[0];if(tag.Action!="ExportTag" || tag.SourceEventId!=first || tag.State.Serial!="000123")throw new Exception("Tag export was not recorded");
            form.ExportCodeFile(Path.Combine(testFolder,"actual-code.png"));
            form.ExportTagFile(Path.Combine(testFolder,"actual.zpl"),true);
            if(store.Query().Count!=before+3 || store.Query()[0].Action!="ExportZpl")throw new Exception("Successful exports missing history");
            int successful=store.Query().Count;
            try{form.ExportCodeFile(Path.Combine(testFolder,"missing-dir","code.png"));throw new Exception("Bad export path accepted");}catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or DirectoryNotFoundException){}
            if(store.Query().Count!=successful)throw new Exception("Failed export was logged as success");
            form.StartNewAsset();
            try{form.ExportCodeFile(Path.Combine(testFolder,"empty.png"));throw new Exception("Blank export accepted");}catch(ArgumentException){}
            if(store.Query().Count!=successful)throw new Exception("Preview, clear or invalid export created history");
            form.Restore(new WorkState{AssetType="Monitor",Brand="Dell",Model="H22203H",Serial="ABC123",PartNumber="ZYX123",MacAddress="00:1A:2B:3C:4D:5E",Code=1});
            form.ExportTagFile(Path.Combine(testFolder,"sample-label.png"));
            form.ShowReports();
            if(form.Reports.VisibleRows().Count!=store.Query().Count)throw new Exception("Restoring a label unexpectedly enabled date filters");
            form.Restore(form.CaptureWork() with {Language="es"});form.ShowReports();
            if(form.Reports.VisibleRows().Count!=store.Query().Count || form.AboutTooltip!="Acerca de")throw new Exception("Language change modified filters or About tooltip");
            form.Close();
        }
        using(var demo=new MainForm(Path.Combine(testFolder,"demo-work.json"),AppConfig.CreateDefault(),Path.Combine(testFolder,"demo.db")))
        {
            demo.Show();
            demo.Restore(new WorkState{AssetType="Monitor",Brand="Dell",Model="H22203H",Serial="ABC123",PartNumber="ZYX123",MacAddress="00:1A:2B:3C:4D:5E",Code=1});
            demo.ExportTagFile(Path.Combine(testFolder,"demo-monitor.png"));demo.ExportCodeFile(Path.Combine(testFolder,"demo-qr.png"));
            demo.Restore(new WorkState{AssetType="Laptop",Brand="Lenovo",Model="ThinkPad T14",Serial="LT000456",PartNumber="T14-DEMO",MacAddress="00:11:22:33:44:55"});demo.ExportTagFile(Path.Combine(testFolder,"demo-laptop.png"));
            demo.ShowReports();Application.DoEvents();
            using(var capture=new Bitmap(demo.Width,demo.Height)){demo.DrawToBitmap(capture,new Rectangle(Point.Empty,demo.Size));capture.Save(Path.Combine(folder,"reports-en.png"));}
            demo.Restore(demo.CaptureWork() with {Language="es"});demo.ShowReports();Application.DoEvents();
            if(demo.Reports.VisibleRows().Count!=3)throw new Exception("Language change unexpectedly filtered demo reports");
            using(var capture=new Bitmap(demo.Width,demo.Height)){demo.DrawToBitmap(capture,new Rectangle(Point.Empty,demo.Size));capture.Save(Path.Combine(folder,"reports-es.png"));}
            demo.Close();
        }
    }
}
