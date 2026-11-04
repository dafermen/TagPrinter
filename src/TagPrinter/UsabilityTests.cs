using ZXing;
namespace TagPrinter;

/// <summary>Tests centering, destructive recycle-bin boundaries and read-only status UI without physical printers.</summary>
internal static class UsabilityTests
{
    /// <summary>Runs real rendering/SQLite/UI checks with synthetic state and a fake spooler-status provider.</summary>
    internal static void Run(string folder)
    {
        TestCentering(folder);TestPurge(folder);TestPrinterStatus(folder);
    }

    /// <summary>Measures actual dark-pixel bounds and decodes centered labels at both supported resolutions.</summary>
    private static void TestCentering(string folder)
    {
        foreach(int dpi in new[]{203,300})
        {
            var data=new LabelData("HDMI","Cables","",50,100,dpi,3,false,ShowSerialText:false,ShowCode:false,CenterText:true);
            using var centered=LabelRenderer.Render(data);
            int left=centered.Width,top=centered.Height,right=0,bottom=0;
            for(int y=0;y<centered.Height;y++)for(int x=0;x<centered.Width;x++)if(centered.GetPixel(x,y).GetBrightness()<.5f){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
            if(Math.Abs((left+right)/2.0-centered.Width/2.0)>dpi*.06 || Math.Abs((top+bottom)/2.0-centered.Height/2.0)>dpi*.06)throw new Exception("Text block not centered");
            centered.Save(Path.Combine(folder,$"centered-text-{dpi}.png"));
            using var withCode=LabelRenderer.Render(new LabelData("Monitor","Dell","CENTER123",100,50,dpi,3,true,CenterText:true));
            var rgb=new byte[withCode.Width*withCode.Height*3];
            for(int y=0;y<withCode.Height;y++)for(int x=0;x<withCode.Width;x++){var c=withCode.GetPixel(x,y);int i=(y*withCode.Width+x)*3;rgb[i]=c.R;rgb[i+1]=c.G;rgb[i+2]=c.B;}
            var decoded=new BarcodeReaderGeneric{Options=new ZXing.Common.DecodingOptions{TryHarder=true}}.Decode(new RGBLuminanceSource(rgb,withCode.Width,withCode.Height,RGBLuminanceSource.BitmapFormat.RGB24));
            if(decoded?.Text!="CENTER123")throw new Exception("Centered text overlaps code");
        }
        if(System.Text.Json.JsonSerializer.Deserialize<WorkState>("{}")!.CenterText)throw new Exception("Legacy alignment changed");
        string root=Path.Combine(folder,"centering-"+Guid.NewGuid());Directory.CreateDirectory(root);
        using var form=new MainForm(Path.Combine(root,"settings.json"),AppConfig.CreateDefault());
        form.Restore(new WorkState{AssetType="HDMI",Brand="Cables",Model="",Serial="",ShowCode=false,CenterText=true,Width=50,Height=100,LabelPresetId="custom",Language="es"});
        string export=Path.Combine(Path.GetTempPath(),"TagPrinter-centered-"+Guid.NewGuid()+".json");form.ExportConfigurationFile(export);
        if(!ConfigurationTransfer.Load(export).Work.CenterText)throw new Exception("Configuration transfer lost centering");
        form.ExportTagFile(Path.Combine(root,"centered.png"));var row=form.History.Query().Single();
        form.Restore(form.CaptureWork() with {CenterText=false});form.LoadHistoryEvent(row);
        if(!form.CaptureWork().CenterText)throw new Exception("History lost centering");
        form.Show();form.assetSection.Expanded=false;form.textSection.Expanded=true;Application.DoEvents();
        using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,form.ClientRectangle);image.Save(Path.Combine(folder,"centered-ui-es.png"));
        form.Close();
    }

    /// <summary>Purges parents and children safely, preserves active rows, rejects restoration and clears loaded provenance.</summary>
    private static void TestPurge(string folder)
    {
        string root=Path.Combine(folder,"purge-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var store=new HistoryStore(Path.Combine(root,"events.db"));
        long parent=store.Add("ExportTag",new WorkState{Serial="PARENT"});
        long active=store.Add("ExportTag",new WorkState{Serial="ACTIVE"},sourceEventId:parent);
        long recycled=store.Add("ExportTag",new WorkState{Serial="RECYCLED"},sourceEventId:parent);
        store.SetDeleted([parent,recycled],true);
        if(store.RecycleBinCount()!=2 || store.EmptyRecycleBin()!=2 || store.EmptyRecycleBin()!=0)throw new Exception("Recycle-bin purge count failed");
        if(store.Get(parent)!=null || store.Get(recycled)!=null || store.Query().Single().Id!=active || store.Get(active)!.SourceEventId!=null)throw new Exception("Purge changed active data or left dangling references");
        store.SetDeleted([parent],false);if(store.Get(parent)!=null)throw new Exception("Purged row restored");
        using var form=new MainForm(Path.Combine(root,"settings.json"),AppConfig.CreateDefault(),store.DatabasePath);
        long loaded=store.Add("ExportTag",new WorkState{Serial="LOADED"});form.LoadHistoryEvent(store.Get(loaded)!);store.SetDeleted([loaded],true);
        if(form.Reports.PurgeRecycleBin()!=1)throw new Exception("Reports purge failed");
        form.ExportTagFile(Path.Combine(root,"after-purge.png"));
        if(store.Query()[0].SourceEventId!=null)throw new Exception("Loaded deleted source was retained");
        long demo=store.Add("ExportTag",new WorkState{Serial="TO-RECYCLE"});store.SetDeleted([demo],true);
        form.Show();form.ShowReports();
        var recycle=form.Reports.Controls.OfType<Control>().SelectMany(Children).OfType<CheckBox>().Single();recycle.Checked=true;Application.DoEvents();
        using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,form.ClientRectangle);image.Save(Path.Combine(folder,"recycle-bin-en.png"));
        form.Close();
    }

    /// <summary>Traverses controls to exercise visible selectors without adding production-only test commands.</summary>
    private static IEnumerable<Control> Children(Control root)
    {
        yield return root;foreach(Control child in root.Controls)foreach(var descendant in Children(child))yield return descendant;
    }

    /// <summary>Checks Windows bit mapping, selection races and timeout UI with no native printer access or jobs.</summary>
    private static void TestPrinterStatus(string folder)
    {
        foreach(var test in new[]{(0u,"ready"),(0x80u,"offline"),(0x10u,"paper"),(0x8u,"jam"),(1u,"paused"),(0x400u,"printing"),(0x1000000u,"sleep"),(0x800000u,"unknown")})
            if(WindowsPrinterStatus.Interpret(test.Item1,0,0).Key!=test.Item2)throw new Exception("Windows status mapping failed");
        if(WindowsPrinterStatus.Interpret(0,0x400,0).Key!="offline" || WindowsPrinterStatus.Interpret(0,0,0,"PORTPROMPT:").Key!="virtual")throw new Exception("Queue attribute/virtual mapping failed");
        var gate=new TaskCompletionSource<PrinterStatusSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;
        string path=Path.Combine(folder,"printer-state-"+Guid.NewGuid()+".json");
        using(var form=new MainForm(path,AppConfig.CreateDefault(),null,name=>{calls++;return name=="Fake A"?gate.Task:Task.FromResult(new PrinterStatusSnapshot("offline",CheckedUtc:DateTimeOffset.UtcNow));}))
        {
            form.Show();var combo=form.Controls.Find("PrinterSelector",true).OfType<ComboBox>().Single();
            combo.Items.Add("Fake A");combo.Items.Add("Fake B");combo.SelectedItem="Fake A";
            combo.SelectedItem="Fake B";gate.SetResult(new("ready"));
            Pump(()=>form.PrinterStatusText.Contains("Offline"),3000);
            if(calls>3)throw new Exception("Status queries multiplied during selection change");
            form.Restore(new WorkState{Language="es",Printer="Fake B"});Application.DoEvents();
            if(!form.PrinterStatusText.Contains("Sin conexión"))throw new Exception("Status localization failed");
            using var capture=new Bitmap(form.Width,form.Height);form.DrawToBitmap(capture,form.ClientRectangle);capture.Save(Path.Combine(folder,"printer-status-es.png"));
            form.Close();
        }
        var slow=new TaskCompletionSource<PrinterStatusSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);int slowCalls=0;
        using(var form=new MainForm(Path.Combine(folder,"slow-state-"+Guid.NewGuid()+".json"),AppConfig.CreateDefault(),null,_=>{slowCalls++;return slow.Task;}))
        {
            form.Show();var combo=form.Controls.Find("PrinterSelector",true).OfType<ComboBox>().Single();combo.Items.Add("Fake slow");combo.SelectedItem="Fake slow";
            Pump(()=>form.PrinterStatusText.Contains("unknown"),6500);
            _ = form.RefreshPrinterStatusAsync();if(slowCalls!=1)throw new Exception("Hung query spawned additional workers");
            form.Close();slow.SetResult(new("printing"));Application.DoEvents();
        }
        Ui.Language="en";
    }

    /// <summary>Pumps the UI for asynchronous assertions, bounded so an unexpected failure stops verification.</summary>
    private static void Pump(Func<bool> complete,int timeoutMs)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        while(!complete() && watch.ElapsedMilliseconds<timeoutMs){Application.DoEvents();Thread.Sleep(10);}
        if(!complete())throw new Exception("Timed out waiting for printer status UI");
    }
}
