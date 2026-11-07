namespace TagPrinter;

/// <summary>Modal progress/cancellation around a worker-owned CSV export; UI remains responsive until cleanup completes.</summary>
internal sealed class ExportProgressDialog : AppWindow
{
    private readonly CancellationTokenSource cancellation=new();
    private readonly Label message=new(){Dock=DockStyle.Fill,Padding=new Padding(18),AutoSize=false};
    private readonly Button cancel=new(){Text=Ui.T("Cancelar","Cancel"),AutoSize=true,Name="CancelExport"};
    private bool running=true;
    internal long Written {get;private set;}
    internal Exception? Failure {get;private set;}
    /// <summary>Starts only after showing; the supplied worker never reads WinForms controls.</summary>
    internal ExportProgressDialog(Func<IProgress<long>,CancellationToken,long> export)
    {
        Text=Ui.T("Exportando CSV","Exporting CSV");Width=520;Height=190;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,Padding=new Padding(14,0,0,0)};footer.Controls.Add(cancel);Controls.Add(message);Controls.Add(footer);message.Text=Ui.T("Preparando exportación…","Preparing export…");
        cancel.Click+=(_,_)=>CancelExport();FormClosing+=(_,e)=>{if(running){e.Cancel=true;CancelExport();}};FluentIcons.ApplyTree(this);
        Shown+=async(_,_)=>
        {
            var progress=new Progress<long>(count=>{if(!IsDisposed){Written=count;if(!cancellation.IsCancellationRequested)message.Text=string.Format(Ui.T("{0:N0} registros exportados…","{0:N0} records exported…"),count);}});
            try{Written=await Task.Run(()=>export(progress,cancellation.Token));DialogResult=DialogResult.OK;}
            catch(OperationCanceledException){DialogResult=DialogResult.Cancel;}
            catch(Exception error){Failure=error;DialogResult=DialogResult.Abort;}
            finally{running=false;Close();}
        };
    }
    /// <summary>Requests native query interruption and keeps the dialog alive while the staged file is cleaned up.</summary>
    internal void CancelExport(){if(cancellation.IsCancellationRequested)return;cancellation.Cancel();cancel.Enabled=false;message.Text=Ui.T("Cancelando y limpiando el archivo temporal…","Canceling and cleaning up the temporary file…");}
    /// <summary>Runs one reviewed destination export and propagates failures after closing the progress window.</summary>
    internal static bool Run(IWin32Window? owner,Func<IProgress<long>,CancellationToken,long> export)
    {
        using var dialog=new ExportProgressDialog(export);var result=dialog.ShowDialog(owner);if(dialog.Failure is {} failure)throw failure;return result==DialogResult.OK;
    }
    /// <summary>Releases cancellation after the worker and any native callback have completed.</summary>
    /// <param name="disposing">True for explicit managed disposal.</param>
    protected override void Dispose(bool disposing){if(disposing)cancellation.Dispose();base.Dispose(disposing);}
}
