using System.Drawing.Imaging;
namespace TagPrinter;

/// <summary>Checks fixed physical paper guidance against the actual preview/export raster at all quarter turns.</summary>
internal static class PaperFeedTests
{
    /// <summary>Uses isolated forms/history and synthetic 100 by 50 mm stock; never prints.</summary>
    internal static void Run(string folder)
    {
        string root=Path.Combine(folder,"paper-scene-"+Guid.NewGuid());Directory.CreateDirectory(root);
        using var form=new MainForm(Path.Combine(root,"settings.json"),AppConfig.CreateDefault());form.Show();
        Bitmap? fixedHeader=null;RectangleF? fixedPaper=null;
        try
        {
            foreach(int angle in new[]{0,90,180,270})
            {
                var state=new WorkState{FreeTextMode=true,FreeText="HDMI\nCables",Width=100,Height=50,LabelPresetId="custom",ContentRotation=angle,
                    FontSizePt=18,CenterText=true,ShowGuides=false,ShowFeedGuide=true,FeedDirection=3};
                form.Restore(state);Application.DoEvents();
                var preview=Descendants(form).OfType<PaperFeedPreview>().Single();
                var captured=form.CaptureWork();
                if(captured.Width!=100 || captured.Height!=50 || captured.ContentRotation!=angle || !preview.ShowPrinter)throw new Exception("Rotation changed physical media or disabled printer scene");
                if(Descendants(form).OfType<ComboBox>().Any(c=>c.Items.Cast<object>().Any(item=>item.ToString()=="Down ↓")))throw new Exception("Obsolete feed-direction selector remains visible");
                var paper=preview.PaperBounds();if(fixedPaper is {} bounds && paper!=bounds)throw new Exception("Paper moved/resized when only content rotated");fixedPaper=paper;
                using var scene=new Bitmap(preview.Width,preview.Height);preview.DrawToBitmap(scene,preview.ClientRectangle);
                using var header=scene.Clone(new Rectangle(0,0,scene.Width,56),PixelFormat.Format32bppArgb);
                if(fixedHeader is not null && !Equal(header,fixedHeader))throw new Exception("Printer changed with content rotation");
                fixedHeader??=(Bitmap)header.Clone();
                // The same bitmap is exported. The printer, leading-edge stripe and feed arrow exist only in OnPaint.
                string path=Path.Combine(root,$"rotation-{angle}.png");form.ExportTagFile(path);
                using var memory=new MemoryStream();preview.Image!.Save(memory,ImageFormat.Png);
                if(!memory.ToArray().SequenceEqual(File.ReadAllBytes(path)))throw new Exception("Printer scene leaked into output or preview differs from output raster");
                scene.Save(Path.Combine(folder,$"paper-exit-{angle}-en.png"));
                form.templateSection.Expanded=true;Application.DoEvents();
                using var full=new Bitmap(form.Width,form.Height);form.DrawToBitmap(full,form.ClientRectangle);full.Save(Path.Combine(folder,$"paper-exit-ui-{angle}-en.png"));
            }
            form.Restore(form.CaptureWork() with {Language="es",ContentRotation=270});Application.DoEvents();
            using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,form.ClientRectangle);image.Save(Path.Combine(folder,"paper-exit-ui-es.png"));}
            form.SaveConfig();form.Close();
        }
        finally{fixedHeader?.Dispose();}
        using(var reopened=new MainForm(Path.Combine(root,"settings.json"),AppConfig.CreateDefault()))
        {
            var s=reopened.CaptureWork();if(s.Width!=100 || s.Height!=50 || s.ContentRotation!=270 || s.FeedDirection!=3)throw new Exception("Physical size/rotation/legacy compatibility lost on restart");
        }
        File.WriteAllText(Path.Combine(folder,"paper-exit-results.txt"),"PASS: fixed printer/label placement at 0/90/180/270, unchanged 100x50 physical media, no direction selector, exact preview/export raster equality, bilingual screenshots and legacy-state restart. No printing performed.");
    }

    /// <summary>Traverses the actual control tree to find the live preview and old selectors.</summary>
    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;foreach(Control child in root.Controls)foreach(var node in Descendants(child))yield return node;
    }

    /// <summary>Compares printer-header pixels to prove the physical reference stays fixed.</summary>
    private static bool Equal(Bitmap a,Bitmap b)
    {
        if(a.Size!=b.Size)return false;
        for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;
        return true;
    }
}
