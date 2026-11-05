using System.Drawing.Drawing2D;
namespace TagPrinter;

/// <summary>Shows the actual rendered label beneath a fixed printer, with a downward feed and leading edge.</summary>
internal sealed class PaperFeedPreview : PictureBox
{
    /// <summary>Whether to draw the printer scene instead of the ordinary zoomed bitmap.</summary>
    internal bool ShowPrinter {get;set;}=true;
    /// <summary>Physical stock width across the printer, in millimeters.</summary>
    internal decimal StockWidth {get;set;}=100;
    /// <summary>Physical stock length in the feed direction, in millimeters.</summary>
    internal decimal StockHeight {get;set;}=50;
    /// <summary>Constructs a double-buffered, resize-aware preview that owns no extra bitmap.</summary>
    internal PaperFeedPreview(){DoubleBuffered=true;ResizeRedraw=true;}

    /// <summary>Computes the label's physical aspect ratio below the printer; text rotation never enters this calculation.</summary>
    internal RectangleF PaperBounds()
    {
        if(Image is null)return RectangleF.Empty;
        float availableW=Math.Max(1,ClientSize.Width-144),availableH=Math.Max(1,ClientSize.Height-164);
        float scale=Math.Min(availableW/Image.Width,availableH/Image.Height);
        float w=Image.Width*scale,h=Image.Height*scale;
        return new RectangleF((ClientSize.Width-w)/2,104,w,h);
    }

    /// <summary>Paints outside-only guidance and the same renderer bitmap used for printing/exporting.</summary>
    /// <param name="pe">Windows-owned paint context.</param>
    protected override void OnPaint(PaintEventArgs pe)
    {
        if(!ShowPrinter || Image is null || ClientSize.Width<240 || ClientSize.Height<220){base.OnPaint(pe);return;}
        var g=pe.Graphics;g.Clear(BackColor);
        var paper=PaperBounds();float cx=ClientSize.Width/2f;
        using var line=new Pen(Color.FromArgb(35,85,145),2);
        using var ink=new SolidBrush(line.Color);
        using var body=new SolidBrush(Color.FromArgb(200,210,222));
        using var slot=new SolidBrush(Color.FromArgb(70,85,105));
        using var center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
        using var small=new Font(Font.FontFamily,9);
        // The printer is a diagram, not an observation of the selected queue's power or model.
        float printerW=Math.Max(156,paper.Width+24);
        var printer=new RectangleF(cx-printerW/2,6,printerW,48);
        g.FillRectangle(body,printer);g.DrawRectangle(line,printer.X,printer.Y,printer.Width,printer.Height);
        g.DrawString(Ui.T("IMPRESORA","PRINTER"),Font,ink,new RectangleF(printer.X,8,printer.Width,30),center);
        g.FillRectangle(slot,paper.Left,44,paper.Width,6);
        g.DrawString(Ui.T("El papel avanza hacia abajo ↓","Paper feeds downward ↓"),small,ink,new RectangleF(4,57,ClientSize.Width-8,22),center);
        g.DrawString($"{StockWidth:0.#} mm · "+Ui.T("ancho del rollo","across the roll"),small,ink,new RectangleF(4,80,ClientSize.Width-8,22),center);
        // Dashed tracks connect the output slot to the trailing edge. Leading edge is furthest from the printer.
        using var track=new Pen(Color.FromArgb(120,145,170),1){DashStyle=DashStyle.Dash};
        g.DrawLine(track,paper.Left,50,paper.Left,paper.Top);g.DrawLine(track,paper.Right,50,paper.Right,paper.Top);
        g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        g.DrawImage(Image,paper);g.PixelOffsetMode=PixelOffsetMode.Default;
        g.DrawRectangle(line,paper.X,paper.Y,paper.Width,paper.Height);
        using var leading=new Pen(Color.FromArgb(0,118,150),4);
        g.DrawLine(leading,paper.Left,paper.Bottom,paper.Right,paper.Bottom);
        g.DrawString(Ui.T("BORDE QUE SALE PRIMERO","FIRST EDGE OUT"),small,ink,new RectangleF(0,paper.Bottom+5,ClientSize.Width,23),center);
        g.DrawString(Ui.T("Vista de salida · no se imprime","Paper-exit view · not printed"),small,ink,new RectangleF(0,paper.Bottom+29,ClientSize.Width,22),center);
        // A fixed arrow beside the label makes direction independent of any reading orientation.
        float arrowX=paper.Right+28,mid=paper.Top+paper.Height/2;
        g.DrawLine(line,arrowX,mid-24,arrowX,mid+24);
        g.FillPolygon(ink,[new PointF(arrowX,mid+30),new PointF(arrowX-6,mid+19),new PointF(arrowX+6,mid+19)]);
        var saved=g.Save();
        g.TranslateTransform(paper.Left-24,mid);g.RotateTransform(-90);
        g.DrawString($"{StockHeight:0.#} mm · "+Ui.T("avance","feed"),small,ink,new RectangleF(-paper.Height/2,-20,paper.Height,22),center);
        g.Restore(saved);
    }
}
