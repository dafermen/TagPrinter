using System.Drawing.Imaging;
using System.Text;
using ZXing;
using ZXing.Common;
namespace TagPrinter;

/// <summary>
/// Immutable rendering input independent of controls. Distances are millimeters, font sizes are points and
/// Dpi converts measurements to pixels.
/// </summary>
/// <param name="Brand">Manufacturer text to draw when ShowBrand is enabled.</param>
/// <param name="Model">Model description retained even when hidden.</param>
/// <param name="Serial">Symbol payload/asset identifier; may be blank for a code-free label.</param>
/// <param name="WidthMm">Physical width in millimeters.</param>
/// <param name="HeightMm">Physical height in millimeters.</param>
/// <param name="Dpi">Pixels per inch for physical-to-raster conversion.</param>
/// <param name="Layout">0 bottom-center, 1 bottom-left, 2 bottom-right, 3 code beside text.</param>
/// <param name="Qr">True selects QR; false selects Code 128.</param>
/// <param name="AutoFont">Whether fitting may shrink text down to 50 percent.</param>
/// <param name="FontPercent">Manual scale percentage from 50 through 200.</param>
/// <param name="MarginMm">Inner margin from 1 through 5 millimeters.</param>
/// <param name="OffsetXmm">Horizontal shift from -5 through 5 millimeters before rotation.</param>
/// <param name="OffsetYmm">Vertical shift from -5 through 5 millimeters before rotation.</param>
/// <param name="ShowBrand">Whether to print this field; hiding it preserves its value.</param>
/// <param name="ShowModel">Whether to print this field; hiding it preserves its value.</param>
/// <param name="ShowSerialText">Whether to print this field; hiding it preserves its value.</param>
/// <param name="PartNumber">Optional part-number text.</param>
/// <param name="MacAddress">Optional MAC text; not normalized or validated automatically.</param>
/// <param name="ShowPartNumber">Whether to print this field; hiding it preserves its value.</param>
/// <param name="ShowMacAddress">Whether to print this field; hiding it preserves its value.</param>
/// <param name="AssetType">Optional category used as the heading when visible and nonempty.</param>
/// <param name="ShowAssetType">Whether to print this field; hiding it preserves its value.</param>
/// <param name="ShowCode">Whether to draw/encode the symbol. False gives text the entire content area.</param>
/// <param name="FontFamily">Windows font-family name; UI restoration falls back to Arial if absent.</param>
/// <param name="FontSizePt">Body base size 4 through 72 points; heading uses 1.3 times this value.</param>
/// <param name="Bold">Applies bold to every text line.</param>
/// <param name="HeadingBold">Applies heading bold independently of general Bold.</param>
/// <param name="CenterText">Centers text as one block within the text area, horizontally and vertically.</param>
/// <param name="CodePercent">Symbol size relative to the original fitted area, from 25 through 200 percent.</param>
/// <param name="ContentRotation">Clockwise degrees: 0, 90, 180 or 270.</param>
public record LabelData(string Brand, string Model, string Serial, decimal WidthMm, decimal HeightMm, int Dpi, int Layout, bool Qr, bool AutoFont = true, int FontPercent = 100, decimal MarginMm = 2, decimal OffsetXmm = 0, decimal OffsetYmm = 0, bool ShowBrand = true, bool ShowModel = true, bool ShowSerialText = true, string PartNumber = "", string MacAddress = "", bool ShowPartNumber = true, bool ShowMacAddress = true, string AssetType = "", bool ShowAssetType = true, bool ShowCode = true, string FontFamily = "Arial", decimal FontSizePt = 10, bool Bold = false, bool HeadingBold = true, int ContentRotation = 0, int CodePercent = 100, bool CenterText = false);
/// <summary>
/// Draws complete labels and converts bitmaps into raster ZPL. Preview, export and printing share this
/// renderer.
/// </summary>
public static class LabelRenderer
{
    /// <summary>
    /// Validates settings, fits visible text/code and returns a new label bitmap. Quarter turns use an
    /// intermediate with swapped dimensions.
    /// </summary>
    /// <param name="d">Complete rendering settings, independent of UI controls.</param>
    /// <param name="guides">Includes preview-only margin guides; output actions leave this false.</param>
    /// <returns>A new bitmap owned by the caller; dispose after use.</returns>
    /// <exception cref="ArgumentException">Rotation/settings are unsupported, required content is missing, or text/code cannot fit.</exception>
    public static Bitmap Render(LabelData d, bool guides = false)=>Render(d,guides,out _);
    /// <summary>
    /// Validates settings, fits visible text/code and returns a new label bitmap. Quarter turns use an
    /// intermediate with swapped dimensions.
    /// </summary>
    /// <param name="d">Complete rendering settings, independent of UI controls.</param>
    /// <param name="guides">Includes preview-only margin guides; output actions leave this false.</param>
    /// <param name="usedFontPercent">Receives the effective percentage of the configured base text size.</param>
    /// <returns>A new bitmap owned by the caller; dispose after use.</returns>
    /// <exception cref="ArgumentException">Rotation/settings are unsupported, required content is missing, or text/code cannot fit.</exception>
    public static Bitmap Render(LabelData d, bool guides, out int usedFontPercent)
    {
        if(d.ContentRotation is not (0 or 90 or 180 or 270))throw new ArgumentException("Invalid rotation.");
        // Quarter turns render into swapped temporary dimensions, then rotate into the requested physical
        // media. Recursion stops because the temporary rotation is zero.
        if(d.ContentRotation!=0)
        {
            bool quarter=d.ContentRotation is 90 or 270;
            var rotated=Render(d with {WidthMm=quarter?d.HeightMm:d.WidthMm,HeightMm=quarter?d.WidthMm:d.HeightMm,ContentRotation=0},guides,out usedFontPercent);
            rotated.RotateFlip(d.ContentRotation==90?RotateFlipType.Rotate90FlipNone:d.ContentRotation==180?RotateFlipType.Rotate180FlipNone:RotateFlipType.Rotate270FlipNone);
            return rotated;
        }
        if(d.CodePercent<25 || d.CodePercent>200)throw new ArgumentException("Code size must be between 25 and 200 percent.");
        if(d.FontSizePt<4 || d.FontSizePt>72)throw new ArgumentException("Font size must be between 4 and 72 pt.");
        usedFontPercent=100;
        if (d.ShowCode && string.IsNullOrWhiteSpace(d.Serial)) throw new ArgumentException(Ui.T("Escribe un serial.", "Enter a serial number."));
        if (d.ShowCode && !d.Qr && d.Serial.Any(c => c < 32 || c > 126)) throw new ArgumentException(Ui.T("Code 128 admite aquí caracteres ASCII. Usa QR para otros caracteres.", "Use QR for non-ASCII serial numbers."));
        // w/h are bitmap pixels. One inch is 25.4 mm: pixels = millimeters * DPI / 25.4.
        int w = (int)Math.Round((double)d.WidthMm * d.Dpi / 25.4), h = (int)Math.Round((double)d.HeightMm * d.Dpi / 25.4);
        if(d.MarginMm < 1 || d.MarginMm > 5 || d.FontPercent < 50 || d.FontPercent > 200 || Math.Abs(d.OffsetXmm)>5 || Math.Abs(d.OffsetYmm)>5)
            throw new ArgumentException(Ui.T("Los ajustes están fuera del rango permitido.","Settings are outside the supported range."));
        // m is the pixel inset; dx/dy shift the content rectangle. Bounds checks reject shifts that leave
        // the physical bitmap.
        int m = (int)Math.Round((double)d.MarginMm * d.Dpi / 25.4);
        int dx = (int)Math.Round((double)d.OffsetXmm * d.Dpi / 25.4), dy = (int)Math.Round((double)d.OffsetYmm * d.Dpi / 25.4);
        var content = new Rectangle(m+dx, m+dy, w - 2*m, h - 2*m);
        if(content.Left<0 || content.Top<0 || content.Right>w || content.Bottom>h)
            throw new ArgumentException(Ui.T("El desplazamiento supera el espacio disponible. Auméntalo menos o amplía el margen.","The offset exceeds the available space. Reduce it or increase the margin."));
        // Keep only visible nonempty lines. Each tuple holds text and a heading flag; the first available
        // type/brand supplies the heading.
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
        // textW/textH reserve text space. codeArea receives the remainder; code-only output receives the
        // whole inset rectangle.
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
            // One module is one QR cell or barcode unit. Integer scaling keeps edges sharp; scale below one
            // means the symbol cannot fit.
            int fittedScale=Math.Min(codeArea.Width / matrix.Width,codeArea.Height / matrix.Height);
            scale=(int)Math.Floor(fittedScale*d.CodePercent/100.0);
            if(scale<1)throw new ArgumentException(Ui.T("El código queda demasiado pequeño. Aumenta su tamaño o la etiqueta.","The code is too small. Increase its size or the label size."));
            int cw=matrix.Width*scale;
            int baseHeight=d.Qr?matrix.Height*fittedScale:Math.Min(codeArea.Height,d.Dpi/4);
            ch=d.Qr?matrix.Height*scale:Math.Max(1,(int)Math.Round(baseHeight*d.CodePercent/100.0));
            // Growth can borrow text space on the layout's shared axis. Never clip modules or draw outside margins.
            if(side && lineCount>0 && cw>codeArea.Width)
            {
                textW=content.Width-cw-m/2;
                codeArea=new Rectangle(content.Right-cw,content.Top,cw,content.Height);
            }
            else if(!side && lineCount>0 && ch>codeArea.Height)
            {
                textH=content.Height-ch;
                codeArea=new Rectangle(content.Left,content.Bottom-ch,content.Width,ch);
            }
            if(cw>codeArea.Width || ch>codeArea.Height || (lineCount>0 && (textW<=0 || textH<=0)))
                throw new ArgumentException(Ui.T("El código no cabe con ese tamaño. Reduce el porcentaje, cambia la distribución o amplía la etiqueta.","The code does not fit at this size. Reduce the percentage, change layout or enlarge the label."));
            x = d.Layout == 1 ? codeArea.Left : d.Layout == 2 ? codeArea.Right-cw : codeArea.Left+(codeArea.Width-cw)/2;
            y = codeArea.Top + (codeArea.Height-ch)/2;
        }
        var bmp = new Bitmap(w, h); bmp.SetResolution(d.Dpi, d.Dpi);
        try
        {
            using var g = Graphics.FromImage(bmp); g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            using var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.None, Alignment=d.CenterText?StringAlignment.Center:StringAlignment.Near };
            // Points convert to pixels with DPI / 72. textRects gives every visible line an equal-height slot.
            float[] baseSizes = lines.Select(line=>d.Dpi*(float)d.FontSizePt*(line.IsHeading?1.3f:1f)/72f).ToArray();
            var textRects=Enumerable.Range(0,lineCount).Select(i=>new RectangleF(content.Left,content.Top+textH*i/(float)lineCount,textW,textH/(float)lineCount)).ToArray();
            // Measures each line at a proposed scale and checks width/height against its assigned rectangle.
            float gap=d.Dpi/72f; // One point between centered lines; legacy equal-height rows are unchanged.
            float[] MeasureHeights(float candidate)
            {
                var heights=new float[lineCount];
                for(int i=0;i<lineCount;i++)
                {
                    using var font=new Font(d.FontFamily,baseSizes[i]*candidate,(d.Bold || (lines[i].IsHeading && d.HeadingBold))?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
                    var measured=g.MeasureString(lines[i].Text,font,new SizeF(textW,10000),fmt);
                    if(measured.Width>textW)return [];
                    heights[i]=measured.Height;
                }
                return heights;
            }
            bool Fits(float candidate)
            {
                float[] heights=MeasureHeights(candidate);
                if(heights.Length!=lineCount)return false;
                return d.CenterText?heights.Sum()+gap*Math.Max(0,lineCount-1)<=textH:
                    heights.Select((height,i)=>height<=textRects[i].Height).All(fits=>fits);
            }
            // Auto mode starts at 100 percent and shrinks one percentage point at a time to 50. Manual mode
            // uses FontPercent. Both reject remaining overflow.
            float factor=d.AutoFont?1f:d.FontPercent/100f;
            if(d.AutoFont)
            {
                while(!Fits(factor) && factor>.5f)factor=Math.Max(.5f,factor-.01f);
            }
            usedFontPercent=(int)Math.Round(factor*100);
            if(!Fits(factor))throw new ArgumentException(Ui.T("El texto no cabe. Reduce la fuente con −, activa el ajuste automático o amplía la etiqueta.","Text does not fit. Reduce text size with −, enable automatic fit or use a larger label."));
            if(d.CenterText && lineCount>0)
            {
                // Center the entire measured block, not each line inside a widely spaced legacy row.
                float[] heights=MeasureHeights(factor);
                float top=content.Top+(textH-heights.Sum()-gap*(lineCount-1))/2;
                for(int i=0;i<lineCount;i++){textRects[i]=new RectangleF(content.Left,top,textW,heights[i]);top+=heights[i]+gap;}
            }
            for(int i=0;i<lineCount;i++)
            {
                using var font=new Font(d.FontFamily,baseSizes[i]*factor,(d.Bold || (lines[i].IsHeading && d.HeadingBold))?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
                g.DrawString(lines[i].Text,font,Brushes.Black,textRects[i],fmt);
            }
            // Paint individual modules as solid rectangles rather than stretching an image, preserving
            // scanner-readable edges.
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
    /// <summary>
    /// Packs dark pixels into hexadecimal bytes and wraps them in raster ZPL with dimensions and requested
    /// quantity.
    /// </summary>
    /// <param name="image">Existing bitmap to read; ownership stays with the caller.</param>
    /// <param name="copies">Requested quantity for the ZPL print command.</param>
    /// <returns>ASCII-compatible raster ZPL with requested quantity.</returns>
    public static string ToZpl(Bitmap image, int copies)
    {
        // stride is bytes per raster row, rounded up: each byte packs eight monochrome pixels. Unused
        // trailing bits stay white.
        int stride=(image.Width+7)/8;
        var hex = new StringBuilder();
        for(int y=0;y<image.Height;y++) for(int b=0;b<stride;b++)
        {
            // The leftmost pixel uses bit 7 (128). Dark pixels set bits; hexadecimal byte pairs form the ZPL
            // ^GFA payload.
            int value=0;
            for(int bit=0;bit<8;bit++) { int x=b*8+bit; if(x<image.Width && image.GetPixel(x,y).GetBrightness()<.5f) value|=128>>bit; }
            hex.Append(value.ToString("X2"));
        }
        int bytes=stride*image.Height;
        return $"^XA^PW{image.Width}^LL{image.Height}^LH0,0^FO0,0^GFA,{bytes},{bytes},{stride},{hex}^FS^PQ{copies}^XZ";
    }
}
