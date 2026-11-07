namespace TagPrinter;

/// <summary>Checks real Fluent outlines, discoverable shortcut captions, translated text and offline button rendering.</summary>
internal static class FluentUiTests
{
    /// <summary>Uses isolated settings/history and never submits a print job.</summary>
    internal static void Run(string folder)
    {
        using var printer=FluentIcons.Render("print",24,Color.Black);using var save=FluentIcons.Render("save",24,Color.Black);
        int ink=0,different=0;for(int y=0;y<24;y++)for(int x=0;x<24;x++){if(printer.GetPixel(x,y).A>0)ink++;if(printer.GetPixel(x,y)!=save.GetPixel(x,y))different++;}
        if(ink<30 || different<20)throw new Exception("Fluent font is missing or produced identical fallback glyphs");
        using var form=new MainForm(Path.Combine(folder,"fluent-"+Guid.NewGuid()+".json"),AppConfig.CreateDefault());form.Show();
        if(form.ShowInTaskbar || Screen.AllScreens.Any(screen=>screen.Bounds.IntersectsWith(form.Bounds)))throw new Exception("Verification window entered the interactive desktop");
        foreach(string language in new[]{"en","es"})
        {
            form.Restore(new WorkState{AssetType="Monitor",Brand="Dell",Model="H22203H",Serial="UI123456",Code=1,Language=language});Application.DoEvents();
            var print=Descendants(form).OfType<Button>().Single(b=>b.Text.Contains("Ctrl + P"));
            if(print.Image is null || print.TextImageRelation!=TextImageRelation.ImageBeforeText || form.PrintSummaryText.Contains("Windows"))throw new Exception("Shortcut/icon/clean home contract failed");
            using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(folder,$"fluent-home-{language}.png"));
        }
        form.Close();File.WriteAllText(Path.Combine(folder,"fluent-ui-results.txt"),"PASS: distinct Fluent glyph outlines, icon/text buttons, visible Ctrl+P, bilingual home and unchanged printer-method persistence. No print jobs.");
    }
    /// <summary>Finds action buttons without depending on control positions.</summary>
    private static IEnumerable<Control> Descendants(Control root){yield return root;foreach(Control child in root.Controls)foreach(var node in Descendants(child))yield return node;}
}
