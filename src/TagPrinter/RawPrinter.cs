using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace TagPrinter;
/// <summary>
/// Submits ASCII ZPL as a RAW Windows spooler job through native interop. Submission does not confirm
/// physical output.
/// </summary>
public static class RawPrinter
{
    // DocInfo matches native DOC_INFO_1 layout: Name is a caption, Output is null for normal jobs and Type is RAW.
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct DocInfo
    {
        // Human-readable document name shown by the spooler.
        public string Name;
        // Optional output-file path; null submits to the selected printer.
        public string? Output;
        // Native datatype string: RAW means bytes are already printer commands.
        public string Type;
    }
    // P/Invoke bridges C# to Windows. SetLastError lets Win32Exception describe the failing native operation.
    // OpenPrinter: Gets a spooler handle; defaults is null here. False means native failure.
    [DllImport("winspool.drv", EntryPoint="OpenPrinterW", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool OpenPrinter(string name,out IntPtr handle,IntPtr defaults);
    // ClosePrinter: Releases a previously opened spooler handle.
    [DllImport("winspool.drv",SetLastError=true)] static extern bool ClosePrinter(IntPtr handle);
    // StartDocPrinter: Starts a job using level 1 DocInfo and returns its spooler job ID; zero means failure.
    [DllImport("winspool.drv",EntryPoint="StartDocPrinterW",SetLastError=true,CharSet=CharSet.Unicode)] static extern int StartDocPrinter(IntPtr handle,int level,ref DocInfo info);
    // EndDocPrinter: Ends the current document scope.
    [DllImport("winspool.drv",SetLastError=true)] static extern bool EndDocPrinter(IntPtr handle);
    // StartPagePrinter: Starts a page in the active document.
    [DllImport("winspool.drv",SetLastError=true)] static extern bool StartPagePrinter(IntPtr handle);
    // EndPagePrinter: Ends the current page scope.
    [DllImport("winspool.drv",SetLastError=true)] static extern bool EndPagePrinter(IntPtr handle);
    // WritePrinter: Submits count bytes and reports accepted byte count; partial writes are treated as failure.
    [DllImport("winspool.drv",SetLastError=true)] static extern bool WritePrinter(IntPtr handle,byte[] bytes,int count,out int written);
    /// <summary>
    /// Opens a printer and RAW document/page, verifies every ASCII byte was accepted and closes native
    /// scopes with nested finally blocks.
    /// </summary>
    /// <param name="printer">Installed Windows printer name for RAW submission.</param>
    /// <param name="zpl">ASCII ZPL commands sent directly as RAW spooler bytes.</param>
    public static void Send(string printer,string zpl)
    {
        // Opening a handle does not print. Each successful handle/document/page scope has a matching finally
        // cleanup below.
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
