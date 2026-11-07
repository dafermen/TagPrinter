using System.ComponentModel;
using System.Runtime.InteropServices;
namespace TagPrinter;

/// <summary>One spooler observation, not proof of physical power or successful paper output.</summary>
internal sealed record PrinterStatusSnapshot(string Key,uint Jobs=0,string Detail="",DateTimeOffset? CheckedUtc=null,uint RawStatus=0);

/// <summary>Read-only Windows spooler status. Native calls must run off the UI thread because drivers may block.</summary>
internal static class WindowsPrinterStatus
{
    // Native PRINTER_INFO_2 contains thirteen pointers followed by eight DWORDs; never dereference after freeing its buffer.
    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        // Server, printer, share and device/driver metadata pointers owned by the GetPrinter buffer.
        public IntPtr Server,Name,Share,Port,Driver,Comment,Location,DevMode,Separator,Processor,DataType,Parameters,Security;
        // Windows bit flags, scheduler values, current status and queued job count.
        public uint Attributes,Priority,DefaultPriority,StartTime,UntilTime,Status,Jobs,AveragePpm;
    }

    // These APIs only read a queue; they never create/delete jobs or change driver settings.
    [DllImport("winspool.drv",EntryPoint="OpenPrinterW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool OpenPrinter(string name,out IntPtr handle,IntPtr defaults);
    [DllImport("winspool.drv",EntryPoint="GetPrinterW",SetLastError=true)]
    private static extern bool GetPrinter(IntPtr handle,uint level,IntPtr buffer,uint size,out uint needed);
    [DllImport("winspool.drv",SetLastError=true)]
    private static extern bool ClosePrinter(IntPtr handle);

    /// <summary>Reads a cached Windows queue observation, releasing both unmanaged buffer and handle on every path.</summary>
    internal static PrinterStatusSnapshot Read(string name)
    {
        if(string.IsNullOrWhiteSpace(name))return new("none");
        IntPtr handle=IntPtr.Zero,buffer=IntPtr.Zero;
        try
        {
            if(!OpenPrinter(name,out handle,IntPtr.Zero))throw new Win32Exception();
            GetPrinter(handle,2,IntPtr.Zero,0,out uint needed);
            if(needed<Marshal.SizeOf<PrinterInfo2>() || needed>16*1024*1024)throw new InvalidDataException("Invalid Windows printer status buffer.");
            buffer=Marshal.AllocHGlobal(checked((int)needed));
            if(!GetPrinter(handle,2,buffer,needed,out _))throw new Win32Exception();
            var info=Marshal.PtrToStructure<PrinterInfo2>(buffer);
            string port=Marshal.PtrToStringUni(info.Port)??"",driver=Marshal.PtrToStringUni(info.Driver)??"";
            return Interpret(info.Status,info.Attributes,info.Jobs,port,driver,DateTimeOffset.UtcNow);
        }
        catch(Exception error){return new("unknown",Detail:error.Message,CheckedUtc:DateTimeOffset.UtcNow);}
        finally {if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(handle!=IntPtr.Zero)ClosePrinter(handle);}
    }

    /// <summary>Maps documented status/attribute bits without inferring electrical power from a successful API call.</summary>
    internal static PrinterStatusSnapshot Interpret(uint flags,uint attributes,uint jobs,string port="",string driver="",DateTimeOffset? checkedUtc=null)
    {
        string key;
        if((flags&(0x80|0x2000000))!=0 || (attributes&0x400)!=0)key="offline";
        else if((flags&0x800000)!=0)key="unknown"; // PRINT_SERVER_UNKNOWN.
        else if((flags&0x8)!=0)key="jam";
        else if((flags&0x10)!=0)key="paper";
        else if((flags&0x400000)!=0)key="door";
        else if((flags&0x40000)!=0)key="toner";
        else if((flags&0x800)!=0)key="bin";
        else if((flags&(0x2|0x40|0x1000|0x100000|0x200000|0x4000000))!=0)key="error";
        else if((flags&1)!=0)key="paused";
        else if((flags&0x400)!=0)key="printing";
        else if((flags&0x1000000)!=0)key="sleep";
        else if((flags&0x20000)!=0)key="low-toner";
        else if((flags&(0x200|0x2000|0x4000|0x8000|0x20|0x10000))!=0)key="busy";
        else if(port.Contains("PORTPROMPT",StringComparison.OrdinalIgnoreCase) || port.Equals("FILE:",StringComparison.OrdinalIgnoreCase) || driver.Contains("Microsoft Print To PDF",StringComparison.OrdinalIgnoreCase) || driver.Contains("XPS",StringComparison.OrdinalIgnoreCase))key="virtual";
        else if(flags!=0)key="unknown";
        else key="ready"; // Zero means no error reported by Windows; it is not a hardware power sensor.
        return new(key,jobs,CheckedUtc:checkedUtc??DateTimeOffset.UtcNow,RawStatus:flags);
    }

    /// <summary>Translates the observation at display time, keeping cached keys independent of UI language.</summary>
    internal static string Caption(PrinterStatusSnapshot value)=>value.Key switch
    {
        "none"=>Ui.T("Sin seleccionar","Not selected"),"checking"=>Ui.T("Consultando…","Checking…"),
        "ready"=>Ui.T("Lista","Ready"),"offline"=>Ui.T("Sin conexión","Offline"),
        "paused"=>Ui.T("En pausa","Paused"),"printing"=>Ui.T("Imprimiendo","Printing"),"busy"=>Ui.T("Ocupada / esperando","Busy / waiting"),
        "sleep"=>Ui.T("Ahorro de energía","Power saving"),"paper"=>Ui.T("Sin papel","Out of paper"),"jam"=>Ui.T("Atasco de papel","Paper jam"),
        "door"=>Ui.T("Cubierta abierta","Cover open"),"toner"=>Ui.T("Sin tóner","Out of toner"),"low-toner"=>Ui.T("Tóner bajo","Low toner"),
        "bin"=>Ui.T("Bandeja de salida llena","Output bin full"),"error"=>Ui.T("Error / requiere atención","Error / needs attention"),
        "virtual"=>Ui.T("Impresora virtual","Virtual printer"),_=>Ui.T("Estado desconocido","Status unknown")
    };
}
