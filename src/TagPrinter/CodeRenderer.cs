using ZXing;
namespace TagPrinter;

/// <summary>Draws standalone QR or Code 128 images with integer modules and white scanner quiet zones.</summary>
public static class CodeRenderer
{
    /// <summary>
    /// Encodes a standalone image with quiet zones and whole-pixel modules. Rejects empty serials,
    /// unsupported DPI and non-ASCII Code 128.
    /// </summary>
    /// <param name="serial">Serial to encode, independent of serial-text visibility.</param>
    /// <param name="qr">True selects QR; false selects printable-ASCII Code 128.</param>
    /// <param name="dpi">Pixels per inch; standalone output supports 203 or 300.</param>
    /// <returns>A new bitmap owned by the caller; dispose after use.</returns>
    /// <exception cref="ArgumentException">Serial is empty, Code 128 contains non-ASCII characters, or DPI is unsupported.</exception>
    public static Bitmap Render(string serial, bool qr, int dpi)
    {
        if(string.IsNullOrWhiteSpace(serial))throw new ArgumentException(Ui.T("Escribe un serial.","Enter a serial number."));
        if(!qr && serial.Any(c=>c<32 || c>126))throw new ArgumentException(Ui.T("Code 128 admite aquí caracteres ASCII. Usa QR para otros caracteres.","Use QR for non-ASCII serial numbers."));
        if(dpi is not (203 or 300))throw new ArgumentException(Ui.T("DPI admitidos: 203, 300.","Supported DPI: 203, 300."));
        // Request an unscaled symbol without library margin, then add explicit quiet zones ourselves.
        var matrix=new MultiFormatWriter().encode(serial,qr?BarcodeFormat.QR_CODE:BarcodeFormat.CODE_128,0,0,
            new Dictionary<EncodeHintType,object>{[EncodeHintType.MARGIN]=0,[EncodeHintType.CHARACTER_SET]="UTF-8"});
        // module is pixels per cell: 4 at 203 DPI, 6 at 300 DPI. QR quiet zones are four cells; barcode
        // horizontal quiet zones are ten.
        int module=Math.Max(4,(int)Math.Round(dpi/50.8));
        int quietX=(qr?4:10)*module, quietY=4*module;
        // QR remains square. Barcode bars use about 12 mm height instead of the encoder matrix row count.
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
        // Bitmap ownership transfers only on success. On drawing failure, release it before propagating the
        // exception.
        catch {image.Dispose();throw;}
    }
}
