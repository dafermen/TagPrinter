using System.Text.Json;
namespace TagPrinter;

/// <summary>Checks reclaimed text space, complete wrapped headings, free-text snapshots and preview-only feed rails.</summary>
internal static class LayoutImprovementsTests
{
    /// <summary>Uses only synthetic labels, isolated SQLite/settings and exported pixels; never prints.</summary>
    internal static void Run(string folder)
    {
        foreach(int dpi in new[]{203,300})foreach(bool qr in new[]{false,true})foreach(int layout in new[]{0,3})
        {
            var data=new LabelData("","","123456",100,50,dpi,layout,qr,ShowSerialText:false,AssetType:"Inventory cables and accessories",FontSizePt:18);
            using var large=LabelRenderer.Render(data,false,out int largeFont);
            using var small=LabelRenderer.Render(data with {CodePercent=50},false,out int smallFont);
            if(smallFont<largeFont)throw new Exception("Shrinking code reduced text space");
            // The lower/right text boundary is independently checked through fitting: a fixed-size heading
            // that cannot fit next to the large symbol must fit after that symbol releases its reserved width.
            if(qr && layout==3)
            {
                bool reclaimedInk=false;
                for(int y=dpi/10;y<dpi/3;y++)for(int x=(int)(small.Width*.56);x<(int)(small.Width*.72);x++)
                    if(small.GetPixel(x,y).GetBrightness()<.5f)reclaimedInk=true;
                if(!reclaimedInk)throw new Exception("Text did not occupy reclaimed QR space");
            }
            small.Save(Path.Combine(folder,$"reclaimed-{dpi}-{qr}-{layout}.png"));
        }
        var heading=new LabelData("","","",50,50,203,3,false,ShowCode:false,AssetType:"Inventory of HDMI cables and adapters for meeting room A",AutoFont:false,FontPercent:100,FontSizePt:10);
        using var wrapped=LabelRenderer.Render(heading);using var changed=LabelRenderer.Render(heading with {AssetType=heading.AssetType[..^1]+"B"});
        if(Equal(wrapped,changed))throw new Exception("Wrapped heading lost its final character");
        wrapped.Save(Path.Combine(folder,"wrapped-heading.png"));
        var free=new LabelData("Hidden brand","Hidden model","",100,50,203,3,false,FreeTextMode:true,FreeText:"Caja de cables HDMI\nSala de reuniones",CenterText:true);
        using var text=LabelRenderer.Render(free);using var clean=LabelRenderer.Render(free with {Brand="",Model="",AssetType="Hidden",Serial="NONASCII-é"});
        if(!Equal(text,clean))throw new Exception("Free text included hidden fields or code");
        try{using var invalid=LabelRenderer.Render(free with {FreeText=" "});throw new Exception("Empty free text accepted");}catch(ArgumentException){}
        string root=Path.Combine(folder,"layout-"+Guid.NewGuid());Directory.CreateDirectory(root);string work=Path.Combine(root,"settings.json");
        var state=new WorkState{FreeTextMode=true,FreeText="Caja de cables HDMI\nSala de reuniones",Serial="HIDDEN123",ShowCode=true,CenterText=true,ShowFeedGuide=true,FeedDirection=1,Language="es"};
        using(var form=new MainForm(work,AppConfig.CreateDefault()))
        {
            form.Show();form.Restore(state);Application.DoEvents();
            form.ExportTagFile(Path.Combine(root,"guide-on.png"));
            var row=form.History.Query().Single();if(row.State.Serial!="" || row.State.ShowCode || !row.State.FreeTextMode || row.State.FreeText!=state.FreeText)throw new Exception("Free-text event indexed a hidden serial");
            form.Restore(form.CaptureWork() with {ShowFeedGuide=false});form.ExportTagFile(Path.Combine(root,"guide-off.png"));
            if(!File.ReadAllBytes(Path.Combine(root,"guide-on.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"guide-off.png"))))throw new Exception("Feed guide leaked into output");
            form.LoadHistoryEvent(row);if(!form.CaptureWork().FreeTextMode || form.CaptureWork().FreeText!=state.FreeText)throw new Exception("Free-text history restoration failed");
            form.Restore(state);string transfer=Path.Combine(Path.GetTempPath(),"TagPrinter-layout-"+Guid.NewGuid()+".json");
            form.ExportConfigurationFile(transfer);var imported=ConfigurationTransfer.Load(transfer);File.Delete(transfer);
            if(imported.Work.FreeText!=state.FreeText || imported.Work.FeedDirection!=1 || !imported.Work.FreeTextMode)throw new Exception("Transfer lost layout options");
            foreach(string language in new[]{"en","es"})
            {
                form.Restore(state with {Language=language});form.templateSection.Expanded=true;Application.DoEvents();
                using var screenshot=new Bitmap(form.Width,form.Height);form.DrawToBitmap(screenshot,form.ClientRectangle);screenshot.Save(Path.Combine(folder,$"free-text-feed-{language}.png"));
            }
            form.SaveConfig();form.Close();
        }
        using(var reopened=new MainForm(work,AppConfig.CreateDefault()))if(!reopened.CaptureWork().FreeTextMode || reopened.CaptureWork().FeedDirection!=1 || reopened.CaptureWork().FreeText!=state.FreeText)throw new Exception("New options not restored on restart");
        var legacy=JsonSerializer.Deserialize<WorkState>("{}")!;if(legacy.FreeTextMode || legacy.FeedDirection!=0 || !legacy.ShowFeedGuide)throw new Exception("Legacy defaults changed unexpectedly");
        File.WriteAllText(Path.Combine(folder,"layout-improvements-results.txt"),"PASS: reclaimed space at 203/300 DPI, wrapped heading final character, free-text isolation/blank rejection, history/config/restart and feed-guide pixel exclusion.");
    }

    /// <summary>Compares every rendered pixel, independent of PNG metadata.</summary>
    private static bool Equal(Bitmap a,Bitmap b)
    {
        if(a.Size!=b.Size)return false;
        for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;
        return true;
    }
}
