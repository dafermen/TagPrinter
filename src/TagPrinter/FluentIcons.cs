using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace TagPrinter;

/// <summary>Renders pinned Microsoft Fluent outlines from an embedded private font. Nothing is installed or fetched at runtime.</summary>
internal static class FluentIcons
{
    private static readonly PrivateFontCollection font=new();
    private static readonly Dictionary<string,int> glyphs;
    private static readonly FontFamily iconFamily;
    // GDI+ requires the memory backing AddMemoryFont to remain alive as long as its private font family.
    private static readonly IntPtr fontMemory;
    private static readonly ConditionalWeakTable<Button,IconState> owned=new();
    private sealed class IconState
    {
        public string Key="";public int Pixels,Argb;public Bitmap? Image;
    }
    static FluentIcons()
    {
        using var source=typeof(FluentIcons).Assembly.GetManifestResourceStream("TagPrinter.Fluent.ttf")??throw new InvalidDataException("Missing Fluent font.");
        using var memory=new MemoryStream();source.CopyTo(memory);byte[] bytes=memory.ToArray();fontMemory=Marshal.AllocHGlobal(bytes.Length);Marshal.Copy(bytes,0,fontMemory,bytes.Length);font.AddMemoryFont(fontMemory,bytes.Length);iconFamily=font.Families[0];
        using var map=typeof(FluentIcons).Assembly.GetManifestResourceStream("TagPrinter.Fluent.json")??throw new InvalidDataException("Missing Fluent glyph map.");glyphs=JsonSerializer.Deserialize<Dictionary<string,int>>(map)??throw new InvalidDataException("Invalid Fluent map.");
    }
    /// <summary>Creates a transparent antialiased icon at the button's physical DPI; the caller owns the result.</summary>
    internal static Bitmap Render(string key,int pixels,Color color)
    {
        string name="ic_fluent_"+key+"_20_regular";
        if(!glyphs.TryGetValue(name,out int code))throw new InvalidDataException("Unknown Fluent icon: "+name);
        var bitmap=new Bitmap(pixels,pixels);
        try
        {
            using var g=Graphics.FromImage(bitmap);g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
            using var path=new GraphicsPath();path.AddString(char.ConvertFromUtf32(code),iconFamily,(int)FontStyle.Regular,pixels,PointF.Empty,StringFormat.GenericTypographic);
            var b=path.GetBounds();if(b.Width<=0 || b.Height<=0)throw new InvalidDataException("Empty Fluent outline.");
            float scale=(pixels-2)/Math.Max(b.Width,b.Height);
            using var transform=new Matrix(scale,0,0,scale,(pixels-b.Width*scale)/2-b.Left*scale,(pixels-b.Height*scale)/2-b.Top*scale);path.Transform(transform);
            using var brush=new SolidBrush(color);g.FillPath(brush,path);return bitmap;
        }
        catch{bitmap.Dispose();throw;}
    }
    /// <summary>Adds icons without replacing captions, existing GitHub artwork or compact symbol-only navigation.</summary>
    internal static void ApplyTree(Control root)
    {
        if(root is Button button)Apply(button);
        foreach(Control child in root.Controls)ApplyTree(child);
    }
    /// <summary>Updates owned per-button images on caption, DPI, enabled and foreground-color changes.</summary>
    private static void Apply(Button button)
    {
        if(button.Parent is CollapsibleSection || button.IsDisposed || button.Text.Length<3 || (button.Image is not null && !owned.TryGetValue(button,out _)))return;
        if(!owned.TryGetValue(button,out var state))
        {
            state=new();owned.Add(button,state);
            button.TextChanged+=(_,_)=>Apply(button);button.EnabledChanged+=(_,_)=>Apply(button);button.ForeColorChanged+=(_,_)=>Apply(button);button.DpiChangedAfterParent+=(_,_)=>Apply(button);
            button.Disposed+=(_,_)=>{if(state.Image is {} old){button.Image=null;state.Image=null;old.Dispose();}};
        }
        string t=button.Text.ToLowerInvariant();
        string key=t.Contains("edit")||t.Contains("editar")?"edit":t.Contains("movement")||t.Contains("movimiento")?"history":t.Contains("retire")||t.Contains("dar de baja")?"archive":t.Contains("clear filter")||t.Contains("limpiar filtro")?"filter_dismiss":t.Contains("imprim")||t.Contains("print")?"print":t.Contains("duplic")||t.Contains("copy")?"copy":t.Contains("nuevo")||t.Contains("new ")?"add":
            t.Contains("código")||t.Contains(" code")?"qr_code":t.Contains("export")?"arrow_download":t.Contains("import")||t.Contains("restaur")||t.Contains("restore")?"arrow_upload":
            t.Contains("guard")||t.Contains("save")?"save":t.Contains("elimin")||t.Contains("delete")||t.Contains("vaciar")||t.Contains("empty")?"delete":
            t.Contains("actualiz")||t.Contains("refresh")?"arrow_sync":t.Contains("buscar")||t.Contains("search")?"search":t.Contains("backup")||t.Contains("back up")||t.Contains("seguridad")?"database":
            t.Contains("cargar")||t.Contains("load")||t.Contains("aplicar")||t.Contains("apply")?"folder":"document";
        int pixels=Math.Max(16,(int)Math.Round(18*button.DeviceDpi/96d));var color=button.Enabled?button.ForeColor:SystemColors.GrayText;
        button.AccessibleName=button.Text;button.TextImageRelation=TextImageRelation.ImageBeforeText;button.ImageAlign=ContentAlignment.MiddleLeft;button.Padding=new Padding(4,2,4,2);button.MinimumSize=new Size(0,30);
        if(state.Key==key && state.Pixels==pixels && state.Argb==color.ToArgb())return;
        var next=Render(key,pixels,color);var previous=state.Image;state.Key=key;state.Pixels=pixels;state.Argb=color.ToArgb();state.Image=next;button.Image=next;previous?.Dispose();
    }
}
