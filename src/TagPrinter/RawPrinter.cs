using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace TagPrinter;
public static class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct DocInfo { public string Name; public string? Output; public string Type; }
    [DllImport("winspool.drv", EntryPoint="OpenPrinterW", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool OpenPrinter(string name,out IntPtr handle,IntPtr defaults);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv",EntryPoint="StartDocPrinterW",SetLastError=true,CharSet=CharSet.Unicode)] static extern int StartDocPrinter(IntPtr handle,int level,ref DocInfo info);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool EndDocPrinter(IntPtr handle);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool StartPagePrinter(IntPtr handle);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool EndPagePrinter(IntPtr handle);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool WritePrinter(IntPtr handle,byte[] bytes,int count,out int written);
    public static void Send(string printer,string zpl)
    {
        if(!OpenPrinter(printer,out var handle,IntPtr.Zero)) throw new Win32Exception();
        try
        {
            var info=new DocInfo { Name="Etiqueta de inventario",Type="RAW" };
            if(StartDocPrinter(handle,1,ref info)==0) throw new Win32Exception();
            try
            {
                if(!StartPagePrinter(handle)) throw new Win32Exception();
                try { var data=Encoding.ASCII.GetBytes(zpl); if(!WritePrinter(handle,data,data.Length,out int written)||written!=data.Length) throw new Win32Exception(); }
                finally { EndPagePrinter(handle); }
            }
            finally { EndDocPrinter(handle); }
        }
        finally { ClosePrinter(handle); }
    }
}
