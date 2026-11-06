using System.Drawing.Printing;
namespace TagPrinter;

public partial class MainForm
{
    // Submission and tracking share one gate so key repeats cannot accidentally duplicate a label.
    private bool printBusy,printingDisposed;
    private readonly CancellationTokenSource printingLifetime=new();
    private readonly Label jobNotice=new(){Dock=DockStyle.Fill,Visible=false,AutoEllipsis=true,Padding=new Padding(5,2,5,2),BackColor=Color.FromArgb(230,240,250)};
    private readonly RowStyle jobNoticeRow=new(SizeType.Absolute,0);
    private RowStyle? printActionsRow;
    private PrintProgress? lastJobProgress;
    private string jobPrinter="";
    private int printGeneration;
    private bool isolatedPrinting;
    // Test seams replace every native submission/query; they are never persisted or shown as application options.
    internal Func<WorkState,Task<PrintTicket>>? PrintSubmitOverride;
    internal Func<PrintTicket,Task<PrintJobObservation>>? JobReaderOverride;
    internal TimeSpan JobPollInterval=TimeSpan.FromMilliseconds(1500),JobTrackingLimit=TimeSpan.FromMinutes(2),JobReadTimeout=TimeSpan.FromSeconds(5),JobNoticeDuration=TimeSpan.FromSeconds(8);
    /// <summary>Current transient notice for isolated integration tests.</summary>
    internal string PrintNoticeText=>jobNotice.Text;
    /// <summary>Whether the temporary notice is allocated visible space.</summary>
    internal bool PrintNoticeVisible=>jobNoticeRow.Height>0;

    /// <summary>Routes Ctrl+P through the same validated command as the print button, only on Labels.</summary>
    /// <param name="msg">Windows message forwarded by reference.</param>
    /// <param name="keyData">Key and modifiers.</param>
    /// <returns>True when the application consumed Ctrl+P.</returns>
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)=>HandlePrintShortcut(keyData,(msg.LParam.ToInt64()&(1L<<30))!=0)||base.ProcessCmdKey(ref msg,keyData);

    /// <summary>Consumes key repeats without starting a second submission; Reports never prints a hidden draft.</summary>
    internal bool HandlePrintShortcut(Keys keys,bool repeated=false)
    {
        if(keys!=(Keys.Control|Keys.P))return false;
        if(repeated)return true; // WM_KEYDOWN previous-state bit: wait for a fresh press even after a fast job.
        if(ShowingLabel)_ = RequestPrintAsync();
        else status.Text=Ui.T("Abre Etiquetas para imprimir con Ctrl + P.","Open Labels to print with Ctrl + P.");
        return true;
    }

    /// <summary>Captures the current work once, submits off-thread, then tracks only the resulting job.</summary>
    internal async Task RequestPrintAsync()
    {
        if(!ready || IsDisposed || printingDisposed || printBusy || !print.Enabled || !ShowingLabel)return;
        printBusy=true;print.Enabled=false;int generation=++printGeneration;
        var token=printingLifetime.Token;var snapshot=GenerationState();var source=sourceHistoryId;jobPrinter=snapshot.Printer;
        ShowJobProgress(new("sending"));
        try
        {
            if(printers.SelectedIndex<0)throw new InvalidOperationException(Ui.T("Selecciona una impresora instalada en Windows.","Select a Windows printer."));
            // Render even with a fake transport so tests exercise exactly the real content validation path.
            var image=LabelRenderer.Render(Data());
            Task<PrintTicket> submission;
            if(PrintSubmitOverride is {} fake){image.Dispose();submission=fake(snapshot);}
            else if(isolatedPrinting){image.Dispose();throw new InvalidOperationException("Isolated tests cannot submit printer jobs.");}
            else
            {
                string title="TagPrinter "+Guid.NewGuid().ToString("N");
                submission=Task.Run(()=>
                {
                    // The worker owns these resources even if the form closes before Windows responds.
                    using(image)
                    {
                        using var doc=CreatePrintDocument(snapshot);doc.DocumentName=title;doc.PrintController=new StandardPrintController();
                        if(snapshot.Mode==1)return new PrintTicket(snapshot.Printer,title,RawPrinter.Send(snapshot.Printer,LabelRenderer.ToZpl(image,snapshot.Copies),title));
                        int remaining=snapshot.Copies;
                        doc.PrintPage+=(_,e)=>
                        {
                            var g=e.Graphics!;g.TranslateTransform(-e.PageSettings.HardMarginX,-e.PageSettings.HardMarginY);
                            g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
                            g.DrawImage(image,new RectangleF(0,0,(float)(snapshot.Width/25.4m*100),(float)(snapshot.Height/25.4m*100)));
                            e.HasMorePages=--remaining>0;
                        };
                        doc.Print();return new PrintTicket(snapshot.Printer,title);
                    }
                });
            }
            var ticket=await submission;
            if(token.IsCancellationRequested || IsDisposed)return;
            RecordSnapshot("PrintSubmitted","",snapshot,source);SaveConfig();
            ShowJobProgress(new("submitted",JobId:ticket.JobId));
            await TrackPrintJobAsync(ticket,token);
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested) { }
        catch(Exception error)
        {
            if(!token.IsCancellationRequested && !IsDisposed)ShowJobProgress(new("failed",true,Detail:error.Message));
        }
        finally
        {
            printBusy=false;
            if(!token.IsCancellationRequested && !IsDisposed)
            {
                UpdatePreview();
                if(lastJobProgress is {Terminal:true})_ = DismissJobNoticeAsync(generation,token);
            }
        }
    }

    /// <summary>Builds driver settings from immutable values; print submission calls this only on its worker.</summary>
    /// <param name="snapshot">Captured printer and physical stock dimensions.</param>
    /// <returns>Caller-owned document, disposed here if construction fails.</returns>
    private static PrintDocument CreatePrintDocument(WorkState snapshot)
    {
        var doc=new PrintDocument();
        try
        {
            doc.PrinterSettings.PrinterName=snapshot.Printer;
            if(!doc.PrinterSettings.IsValid)throw new InvalidOperationException(Ui.T("La impresora seleccionada no está disponible.","The selected printer is unavailable."));
            doc.DocumentName="TagPrinter label";
            doc.DefaultPageSettings.PaperSize=new PaperSize("TagPrinter label",(int)Math.Round(snapshot.Width/25.4m*100),(int)Math.Round(snapshot.Height/25.4m*100));
            doc.DefaultPageSettings.Margins=new Margins(0,0,0,0);doc.DefaultPageSettings.Landscape=false;
            return doc;
        }
        catch{doc.Dispose();throw;}
    }

    /// <summary>Polls with a bounded lifetime and read timeout; failed tracking never resubmits or changes the history event.</summary>
    private async Task TrackPrintJobAsync(PrintTicket ticket,CancellationToken token)
    {
        var tracker=new PrintJobTracker(ticket.JobId);var started=System.Diagnostics.Stopwatch.StartNew();
        while(started.Elapsed<JobTrackingLimit)
        {
            token.ThrowIfCancellationRequested();PrintJobObservation observation;
            try
            {
                var query=JobReaderOverride is {} fake?fake(ticket):isolatedPrinting?Task.FromResult(new PrintJobObservation(false,Detail:"Isolated test transport.")):WindowsPrintJobs.ReadAsync(ticket);
                if(await Task.WhenAny(query,Task.Delay(JobReadTimeout,token))!=query)
                {
                    token.ThrowIfCancellationRequested();
                    // Observe a late fault without creating another native worker or touching disposed controls.
                    _ = query.ContinueWith(t=>{_ = t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                    observation=new(false,Detail:Ui.T("Windows no respondió a tiempo.","Windows did not respond in time."));
                }
                else observation=await query;
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
            catch(Exception error){observation=new(false,Detail:error.Message);}
            token.ThrowIfCancellationRequested();if(IsDisposed)return;
            var progress=tracker.Observe(observation);ShowJobProgress(progress);
            if(progress.Terminal)return;
            await Task.Delay(JobPollInterval,token);
        }
        ShowJobProgress(new("timeout",true));
    }

    /// <summary>Expands the notice without permanently taking space from the preview.</summary>
    private void ShowJobProgress(PrintProgress progress)
    {
        lastJobProgress=progress;jobNotice.Visible=true;jobNoticeRow.Height=54;
        if(printActionsRow is not null)printActionsRow.Height=212;
        RefreshJobNotice();
    }

    /// <summary>Translates transient state at display time so a language change does not reset monitoring.</summary>
    private void RefreshJobNotice()
    {
        if(lastJobProgress is not {} progress)return;
        jobNotice.Text=jobPrinter+(progress.JobId is {} id?$" · #{id}":"")+"\n"+PrintJobTracker.Caption(progress);
        jobNotice.ForeColor=progress.Key is "failed" or "error" or "offline" or "paper"?Color.Firebrick:Color.FromArgb(35,65,100);
        aboutTip.SetToolTip(jobNotice,jobNotice.Text+(progress.Detail.Length>0?"\n"+progress.Detail:"")+
            "\n"+Ui.T("El estado de Windows no confirma salida física ni RFID.","Windows status does not confirm physical output or RFID."));
    }

    /// <summary>Removes a terminal notice after eight seconds, unless another job has replaced it.</summary>
    private async Task DismissJobNoticeAsync(int generation,CancellationToken token)
    {
        try{await Task.Delay(JobNoticeDuration,token);}catch(OperationCanceledException){return;}
        if(IsDisposed || token.IsCancellationRequested || generation!=printGeneration)return;
        jobNotice.Visible=false;jobNoticeRow.Height=0;lastJobProgress=null;
        if(printActionsRow is not null)printActionsRow.Height=158;
    }

    /// <summary>Stops monitoring/dismissal; does not cancel an already submitted Windows job.</summary>
    private void DisposePrinting(){if(printingDisposed)return;printingDisposed=true;printingLifetime.Cancel();printingLifetime.Dispose();}
}
