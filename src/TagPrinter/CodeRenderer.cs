using ZXing;
namespace TagPrinter;

/// <summary>Standalone code image with integer module scaling and explicit white quiet zones.</summary>
public static class CodeRenderer
{
    public static Bitmap Render(string serial, bool qr, int dpi)
    {
        if(string.IsNullOrWhiteSpace(serial))throw new ArgumentException(Ui.T("Escribe un serial.","Enter a serial number."));
        if(!qr && serial.Any(c=>c<32 || c>126))throw new ArgumentException(Ui.T("Code 128 admite aquí caracteres ASCII. Usa QR para otros caracteres.","Use QR for non-ASCII serial numbers."));
        if(dpi is not (203 or 300))throw new ArgumentException(Ui.T("DPI admitidos: 203, 300.","Supported DPI: 203, 300."));
        var matrix=new MultiFormatWriter().encode(serial,qr?BarcodeFormat.QR_CODE:BarcodeFormat.CODE_128,0,0,
            new Dictionary<EncodeHintType,object>{[EncodeHintType.MARGIN]=0,[EncodeHintType.CHARACTER_SET]="UTF-8"});
        int module=Math.Max(4,(int)Math.Round(dpi/50.8));
        int quietX=(qr?4:10)*module, quietY=4*module;
        int contentHeight=qr?matrix.Height*module:(int)Math.Round(dpi*12.0/25.4);
        var image=new Bitmap(matrix.Width*module+2*quietX,contentHeight+2*quietY);
        image.SetResolution(dpi,dpi);
        try
        {
            using var g=Graphics.FromImage(image);g.Clear(Color.White);
            for(int y=0;y<matrix.Height;y++)for(int x=0;x<matrix.Width;x++)
                if(matrix[x,y])g.FillRectangle(Brushes.Black,quietX+x*module,quietY+(qr?y*module:0),module,qr?module:contentHeight);
            return image;
        }
        catch {image.Dispose();throw;}
    }
}
