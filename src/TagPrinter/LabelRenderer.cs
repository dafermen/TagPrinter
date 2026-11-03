using System.Drawing.Imaging;
using System.Text;
using ZXing;
using ZXing.Common;
namespace TagPrinter;

public record LabelData(string Brand, string Model, string Serial, decimal WidthMm, decimal HeightMm, int Dpi, int Layout, bool Qr, bool AutoFont = true, int FontPercent = 100, decimal MarginMm = 2, decimal OffsetXmm = 0, decimal OffsetYmm = 0, bool ShowBrand = true, bool ShowModel = true, bool ShowSerialText = true, string PartNumber = "", string MacAddress = "", bool ShowPartNumber = true, bool ShowMacAddress = true, string AssetType = "", bool ShowAssetType = true, bool ShowCode = true, string FontFamily = "Arial", decimal FontSizePt = 10, bool Bold = false, bool HeadingBold = true, int ContentRotation = 0);
public static class LabelRenderer
{
    public static Bitmap Render(LabelData d, bool guides = false)=>Render(d,guides,out _);
    public static Bitmap Render(LabelData d, bool guides, out int usedFontPercent)
    {
        if(d.ContentRotation is not (0 or 90 or 180 or 270))throw new ArgumentException("Invalid rotation.");
        if(d.ContentRotation!=0)
        {
            bool quarter=d.ContentRotation is 90 or 270;
            var rotated=Render(d with {WidthMm=quarter?d.HeightMm:d.WidthMm,HeightMm=quarter?d.WidthMm:d.HeightMm,ContentRotation=0},guides,out usedFontPercent);
            rotated.RotateFlip(d.ContentRotation==90?RotateFlipType.Rotate90FlipNone:d.ContentRotation==180?RotateFlipType.Rotate180FlipNone:RotateFlipType.Rotate270FlipNone);
            return rotated;
        }
        if(d.FontSizePt<4 || d.FontSizePt>72)throw new ArgumentException("Font size must be between 4 and 72 pt.");
        usedFontPercent=100;
        if (d.ShowCode && string.IsNullOrWhiteSpace(d.Serial)) throw new ArgumentException(Ui.T("Escribe un serial.", "Enter a serial number."));
        if (d.ShowCode && !d.Qr && d.Serial.Any(c => c < 32 || c > 126)) throw new ArgumentException(Ui.T("Code 128 admite aquí caracteres ASCII. Usa QR para otros caracteres.", "Use QR for non-ASCII serial numbers."));
        int w = (int)Math.Round((double)d.WidthMm * d.Dpi / 25.4), h = (int)Math.Round((double)d.HeightMm * d.Dpi / 25.4);
        if(d.MarginMm < 1 || d.MarginMm > 5 || d.FontPercent < 50 || d.FontPercent > 200 || Math.Abs(d.OffsetXmm)>5 || Math.Abs(d.OffsetYmm)>5)
            throw new ArgumentException(Ui.T("Los ajustes están fuera del rango permitido.","Settings are outside the supported range."));
        int m = (int)Math.Round((double)d.MarginMm * d.Dpi / 25.4);
        int dx = (int)Math.Round((double)d.OffsetXmm * d.Dpi / 25.4), dy = (int)Math.Round((double)d.OffsetYmm * d.Dpi / 25.4);
        var content = new Rectangle(m+dx, m+dy, w - 2*m, h - 2*m);
        if(content.Left<0 || content.Top<0 || content.Right>w || content.Bottom>h)
            throw new ArgumentException(Ui.T("El desplazamiento supera el espacio disponible. Auméntalo menos o amplía el margen.","The offset exceeds the available space. Reduce it or increase the margin."));
        var lines = new List<(string Text, bool IsHeading)>();
        bool hasAssetType=d.ShowAssetType && !string.IsNullOrWhiteSpace(d.AssetType);
        if(hasAssetType)lines.Add((d.AssetType,true));
        if(d.ShowBrand && !string.IsNullOrWhiteSpace(d.Brand))lines.Add((d.Brand,!hasAssetType));
        if(d.ShowModel && !string.IsNullOrWhiteSpace(d.Model))lines.Add((d.Model,false));
        if(d.ShowSerialText && !string.IsNullOrWhiteSpace(d.Serial))lines.Add(("Serial: "+d.Serial,false));
        if(d.ShowPartNumber && !string.IsNullOrWhiteSpace(d.PartNumber))lines.Add(("P/N: "+d.PartNumber,false));
        if(d.ShowMacAddress && !string.IsNullOrWhiteSpace(d.MacAddress))lines.Add(("MAC: "+d.MacAddress,false));
        int lineCount=lines.Count;
        if(!d.ShowCode && lineCount==0)throw new ArgumentException(Ui.T("Incluye al menos un campo con texto o activa el código.","Include at least one text field or enable the code."));
        bool side = d.Layout == 3;
        int textW = !d.ShowCode ? content.Width : side ? (int)(content.Width * (d.Qr ? .54 : .40)) : content.Width;
        int textH = !d.ShowCode ? content.Height : side ? content.Height : (int)(content.Height * .48);
        var codeArea = lineCount==0 ? content : side ? new Rectangle(content.Left + textW + m/2, content.Top, content.Width-textW-m/2, content.Height)
            : new Rectangle(content.Left, content.Top + textH, content.Width, content.Height-textH);
        BitMatrix? matrix=null;int scale=0,ch=0,x=0,y=0;
        if(d.ShowCode)
        {
            BarcodeFormat format = d.Qr ? BarcodeFormat.QR_CODE : BarcodeFormat.CODE_128;
            matrix = new MultiFormatWriter().encode(d.Serial, format, 0, 0,
                new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = d.Qr ? 4 : 10, [EncodeHintType.CHARACTER_SET] = "UTF-8" });
            scale = Math.Min(codeArea.Width / matrix.Width, codeArea.Height / matrix.Height);
            if (scale < 1) throw new ArgumentException(Ui.T("El código no cabe. Aumenta la etiqueta o usa el código abajo.", "The code does not fit. Use a larger label or a bottom layout."));
            int cw = matrix.Width * scale; ch = d.Qr ? matrix.Height * scale : Math.Min(codeArea.Height, d.Dpi / 4);
            x = d.Layout == 1 ? codeArea.Left : d.Layout == 2 ? codeArea.Right-cw : codeArea.Left+(codeArea.Width-cw)/2;
            y = codeArea.Top + (codeArea.Height-ch)/2;
        }
        var bmp = new Bitmap(w, h); bmp.SetResolution(d.Dpi, d.Dpi);
        try
        {
            using var g = Graphics.FromImage(bmp); g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            using var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.None };
            float[] baseSizes = lines.Select(line=>d.Dpi*(float)d.FontSizePt*(line.IsHeading?1.3f:1f)/72f).ToArray();
            var textRects=Enumerable.Range(0,lineCount).Select(i=>new RectangleF(content.Left,content.Top+textH*i/(float)lineCount,textW,textH/(float)lineCount)).ToArray();
            bool Fits(float factor)
            {
                for(int i=0;i<lineCount;i++)
                {
                    using var font=new Font(d.FontFamily,baseSizes[i]*factor,(d.Bold || (lines[i].IsHeading && d.HeadingBold))?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
                    var measured=g.MeasureString(lines[i].Text,font,new SizeF(textRects[i].Width,10000),fmt);
                    if(measured.Width>textRects[i].Width || measured.Height>textRects[i].Height)return false;
                }
                return true;
            }
            float factor=d.AutoFont?1f:d.FontPercent/100f;
            if(d.AutoFont)
            {
                while(!Fits(factor) && factor>.5f)factor=Math.Max(.5f,factor-.01f);
            }
            usedFontPercent=(int)Math.Round(factor*100);
            if(!Fits(factor))throw new ArgumentException(Ui.T("El texto no cabe. Reduce la fuente con −, activa el ajuste automático o amplía la etiqueta.","Text does not fit. Reduce text size with −, enable automatic fit or use a larger label."));
            for(int i=0;i<lineCount;i++)
            {
                using var font=new Font(d.FontFamily,baseSizes[i]*factor,(d.Bold || (lines[i].IsHeading && d.HeadingBold))?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
                g.DrawString(lines[i].Text,font,Brushes.Black,textRects[i],fmt);
            }
            if(matrix is not null)for(int row=0; row<matrix.Height; row++) for(int col=0; col<matrix.Width; col++)
                if(matrix[col,row])
                    g.FillRectangle(Brushes.Black,x+col*scale,d.Qr ? y+row*scale : y,scale,d.Qr ? scale : ch);
            if(guides)
            {
                using var guide=new Pen(Color.FromArgb(60,115,190),Math.Max(1,d.Dpi/150f)){DashStyle=System.Drawing.Drawing2D.DashStyle.Dash};
                g.DrawRectangle(guide,m,m,w-2*m-1,h-2*m-1);
            }
            return bmp;
        }
        catch { bmp.Dispose(); throw; }
    }
    public static string ToZpl(Bitmap image, int copies)
    {
        int stride=(image.Width+7)/8;
        var hex = new StringBuilder();
        for(int y=0;y<image.Height;y++) for(int b=0;b<stride;b++)
        {
            int value=0;
            for(int bit=0;bit<8;bit++) { int x=b*8+bit; if(x<image.Width && image.GetPixel(x,y).GetBrightness()<.5f) value|=128>>bit; }
            hex.Append(value.ToString("X2"));
        }
        int bytes=stride*image.Height;
        return $"^XA^PW{image.Width}^LL{image.Height}^LH0,0^FO0,0^GFA,{bytes},{bytes},{stride},{hex}^FS^PQ{copies}^XZ";
    }
}
