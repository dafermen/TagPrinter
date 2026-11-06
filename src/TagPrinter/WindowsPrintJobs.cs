using System.ComponentModel;
using System.Runtime.InteropServices;
namespace TagPrinter;

/// <summary>Correlated submitted job; a driver job may disappear before its numeric identifier is observed.</summary>
internal sealed record PrintTicket(string Printer,string Document,int? JobId=null);
/// <summary>A read-only observation of exactly one uniquely named job, not the whole queue's state.</summary>
internal sealed record PrintJobObservation(bool Available,bool Found=false,uint Status=0,int? JobId=null,string Detail="");
/// <summary>Language-independent state for the temporary printing banner.</summary>
internal sealed record PrintProgress(string Key,bool Terminal=false,int? JobId=null,string Detail="");

/// <summary>Reads documented JOB_INFO_1 data without changing, canceling or submitting queue jobs.</summary>
internal static class WindowsPrintJobs
{
    // One worker at a time across monitors: a hung Windows call must not spawn unlimited replacement workers.
    private static readonly SemaphoreSlim queryGate=new(1,1);
    [StructLayout(LayoutKind.Sequential)]
    private struct JobInfo1
    {
        public uint Id;
        public IntPtr Printer,Machine,User,Document,DataType,StatusText;
        public uint Status,Priority,Position,TotalPages,PagesPrinted;
        public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;
    }
    [DllImport("winspool.drv",EntryPoint="OpenPrinterW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool OpenPrinter(string name,out IntPtr handle,IntPtr defaults);
    [DllImport("winspool.drv",EntryPoint="EnumJobsW",SetLastError=true)]
    private static extern bool EnumJobs(IntPtr handle,uint first,uint count,uint level,IntPtr buffer,uint size,out uint needed,out uint returned);
    [DllImport("winspool.drv",SetLastError=true)]
    private static extern bool ClosePrinter(IntPtr handle);

    /// <summary>Reads off the UI thread and bounds concurrent native work even after a caller times out.</summary>
    internal static async Task<PrintJobObservation> ReadAsync(PrintTicket ticket)
    {
        if(!await queryGate.WaitAsync(0).ConfigureAwait(false))return new(false,Detail:"A Windows job query is still pending.");
        try{return await Task.Run(()=>Read(ticket)).ConfigureAwait(false);}
        finally{queryGate.Release();}
    }

    /// <summary>Rejects unrelated/reused identifiers even when a queue contains many other jobs.</summary>
    internal static bool Matches(PrintTicket ticket,string title,uint id)=>string.Equals(title,ticket.Document,StringComparison.Ordinal) && (ticket.JobId is not {} known || id==known);

    /// <summary>Matches the unique document title and, when known, the ID to avoid observing another job.</summary>
    private static PrintJobObservation Read(PrintTicket ticket)
    {
        IntPtr handle=IntPtr.Zero,buffer=IntPtr.Zero;
        try
        {
            if(!OpenPrinter(ticket.Printer,out handle,IntPtr.Zero))throw new Win32Exception();
            bool first=EnumJobs(handle,0,10000,1,IntPtr.Zero,0,out uint needed,out _);
            if(!first && Marshal.GetLastWin32Error()!=122)throw new Win32Exception();
            if(needed==0)return new(true);
            if(needed>16*1024*1024)throw new InvalidDataException("Windows queue buffer exceeds the supported limit.");
            buffer=Marshal.AllocHGlobal(checked((int)needed));
            if(!EnumJobs(handle,0,10000,1,buffer,needed,out _,out uint count))throw new Win32Exception();
            int stride=Marshal.SizeOf<JobInfo1>();
            if((long)count*stride>needed)throw new InvalidDataException("Invalid Windows queue buffer.");
            for(int i=0;i<count;i++)
            {
                var job=Marshal.PtrToStructure<JobInfo1>(IntPtr.Add(buffer,i*stride));
                string title=Marshal.PtrToStringUni(job.Document)??"";
                if(!Matches(ticket,title,job.Id))continue;
                return new(true,true,job.Status,checked((int)job.Id),Marshal.PtrToStringUni(job.StatusText)??"");
            }
            return new(true);
        }
        catch(Exception error){return new(false,Detail:error.Message);}
        finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(handle!=IntPtr.Zero)ClosePrinter(handle);}
    }
}

/// <summary>Interprets observations without treating a fast/missing job or Windows completion as physical proof.</summary>
internal sealed class PrintJobTracker
{
    private bool observed;
    private int misses;
    private int? jobId;
    /// <summary>Keeps an already-known RAW ID visible even if Windows cannot observe the job.</summary>
    internal PrintJobTracker(int? knownId=null){jobId=knownId;}
    /// <summary>Updates one job's state; three initial misses allow a briefly delayed spooler observation.</summary>
    internal PrintProgress Observe(PrintJobObservation value)
    {
        if(!value.Available)return new("unavailable",true,jobId,value.Detail);
        if(!value.Found)return observed?new("left",true,jobId):new(++misses>=3?"unobserved":"submitted",misses>=3,jobId);
        observed=true;misses=0;jobId=value.JobId??jobId;
        uint s=value.Status;
        // Errors precede completion so combined flags cannot hide a reported problem.
        string key=(s&(0x2|0x200|0x400))!=0?"error":(s&0x20)!=0?"offline":(s&0x40)!=0?"paper":
            (s&1)!=0?"paused":(s&0x100)!=0?"deleted":(s&0x4)!=0?"deleting":(s&(0x80|0x1000))!=0?"complete":
            (s&0x10)!=0?"printing":(s&8)!=0?"spooling":"queued";
        return new(key,key is "deleted" or "complete",jobId,value.Detail);
    }

    /// <summary>Translates banner states while explicitly separating queue progress from physical output.</summary>
    internal static string Caption(PrintProgress state)=>state.Key switch
    {
        "sending"=>Ui.T("Enviando a Windows…","Sending to Windows…"),
        "submitted"=>Ui.T("Enviado. Buscando el trabajo en la cola…","Submitted. Looking for the job in the queue…"),
        "queued"=>Ui.T("En cola de impresión…","In the print queue…"),
        "spooling"=>Ui.T("Windows está preparando el trabajo…","Windows is spooling the job…"),
        "printing"=>Ui.T("Imprimiendo (informado por Windows)…","Printing (reported by Windows)…"),
        "paused"=>Ui.T("Trabajo en pausa. Revisa la cola de Windows.","Job paused. Check the Windows queue."),
        "offline"=>Ui.T("Trabajo sin conexión. Revisa la impresora.","Job offline. Check the printer."),
        "paper"=>Ui.T("Trabajo detenido: falta papel.","Job waiting: out of paper."),
        "error"=>Ui.T("Windows informa un error en el trabajo. Revisa la cola.","Windows reports a job error. Check the queue."),
        "deleting"=>Ui.T("Windows está retirando el trabajo de la cola…","Windows is removing the job from the queue…"),
        "deleted"=>Ui.T("Windows eliminó el trabajo. Resultado físico sin confirmar.","Windows deleted the job. Physical output is unconfirmed."),
        "complete"=>Ui.T("Windows informa trabajo enviado/completado; verifica la etiqueta.","Windows reports sent/completed; check the label."),
        "left"=>Ui.T("El trabajo salió de la cola. Resultado físico sin confirmar.","Job left the queue. Physical output is unconfirmed."),
        "unobserved"=>Ui.T("Enviado; no se alcanzó a observar en la cola. Verifica la etiqueta.","Submitted; job was not observed in the queue. Check the label."),
        "timeout"=>Ui.T("Seguimiento finalizado. Revisa el trabajo en la cola de Windows.","Tracking ended. Check the job in the Windows queue."),
        "failed"=>Ui.T("Falló el envío. Revisa la cola antes de reintentar.","Sending failed. Check the queue before retrying."),
        _=>Ui.T("Enviado; seguimiento no disponible. Revisa la cola de Windows.","Submitted; tracking unavailable. Check the Windows queue.")
    };
}
