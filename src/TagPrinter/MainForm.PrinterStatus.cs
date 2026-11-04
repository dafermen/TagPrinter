namespace TagPrinter;

public partial class MainForm
{
    // This live observation is never exported as configuration: availability changes independently of saved design.
    readonly Label printerState=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Padding=new Padding(0,3,0,0)};
    readonly Button refreshPrinterState=new(){Text="↻",Width=30,Dock=DockStyle.Fill,FlatStyle=FlatStyle.Flat};
    readonly System.Windows.Forms.Timer printerStateTimer=new(){Interval=15000};
    readonly Func<string,Task<PrinterStatusSnapshot>> statusReader;
    Task<PrinterStatusSnapshot>? statusQuery;
    DateTimeOffset statusQueryStarted;
    int statusGeneration;
    PrinterStatusSnapshot lastPrinterState=new("none");
    bool printerStateDisposed;

    /// <summary>Builds an always-visible status row; one outstanding query prevents unbounded work on hung drivers.</summary>
    private Control CreatePrinterStatusRow()
    {
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,34));
        row.Controls.Add(printerState,0,0);row.Controls.Add(refreshPrinterState,1,0);
        refreshPrinterState.Click+=(_,_)=>_ = RefreshPrinterStatusAsync();
        printerStateTimer.Tick+=(_,_)=>_ = RefreshPrinterStatusAsync();
        Shown+=(_,_)=>{printerStateTimer.Start();_ = RefreshPrinterStatusAsync();};
        return row;
    }

    /// <summary>Reads on a worker, shows a timeout after five seconds, and ignores old selections/disposed forms.</summary>
    internal async Task RefreshPrinterStatusAsync(TimeSpan? timeout=null)
    {
        if(!ready || printerStateDisposed || IsDisposed || !IsHandleCreated)return;
        string name=printers.SelectedIndex>=0?printers.Text:"";
        if(name.Length==0){ShowPrinterStatus(new("none"));return;}
        if(statusQuery is {IsCompleted:false})
        {
            ShowPrinterStatus(DateTimeOffset.UtcNow-statusQueryStarted>TimeSpan.FromSeconds(5)?new("unknown",Detail:Ui.T("La consulta anterior de Windows sigue pendiente.","The previous Windows query is still pending.")):new("checking"));return;
        }
        Task<PrinterStatusSnapshot> query;
        try{query=statusReader(name);}
        catch(Exception error){ShowPrinterStatus(new("unknown",Detail:error.Message));return;}
        int generation=++statusGeneration;
        statusQuery=query;statusQueryStarted=DateTimeOffset.UtcNow;ShowPrinterStatus(new("checking"));
        try
        {
            if(await Task.WhenAny(query,Task.Delay(timeout??TimeSpan.FromSeconds(5)))!=query && !printerStateDisposed && generation==statusGeneration && printers.SelectedIndex>=0)
                ShowPrinterStatus(new("unknown",Detail:Ui.T("Windows no respondió a tiempo; no se bloqueó la aplicación.","Windows did not respond in time; the application remains responsive.")));
            var result=await query;
            if(!printerStateDisposed && !IsDisposed && generation==statusGeneration && name==printers.Text)ShowPrinterStatus(result);
        }
        catch(Exception error){if(!printerStateDisposed && !IsDisposed && generation==statusGeneration && name==printers.Text)ShowPrinterStatus(new("unknown",Detail:error.Message));}
        finally
        {
            if(ReferenceEquals(statusQuery,query))statusQuery=null;
            if(!printerStateDisposed && !IsDisposed && name!=printers.Text)_ = RefreshPrinterStatusAsync();
        }
    }

    /// <summary>Displays translated Windows information without asserting that electrical power is on/off.</summary>
    private void ShowPrinterStatus(PrinterStatusSnapshot value)
    {
        lastPrinterState=value;
        printerState.Text=Ui.T("Estado: ","Status: ")+WindowsPrinterStatus.Caption(value)+(value.CheckedUtc is not null && value.Key!="unknown"?Ui.T(" · En cola: "," · Queued: ")+value.Jobs:"");
        printerState.ForeColor=value.Key switch {"offline" or "jam" or "paper" or "door" or "error" or "toner" or "bin"=>Color.Firebrick,"ready"=>Color.DarkGreen,"printing" or "virtual"=>Color.SteelBlue,_=>Color.DarkGoldenrod};
        string hint=Ui.T("Estado informado por Windows; puede estar en caché y no confirma si la impresora está físicamente encendida. Actualización automática cada 15 segundos.","Windows-reported status may be cached and does not confirm physical power. Refreshes automatically every 15 seconds.");
        if(value.CheckedUtc is { } when)hint+="\n"+Ui.T("Consultado: ","Checked: ")+when.LocalDateTime.ToString("HH:mm:ss");
        if(value.Detail.Length>0)hint+="\n"+value.Detail;
        aboutTip.SetToolTip(printerState,hint);aboutTip.SetToolTip(refreshPrinterState,Ui.T("Actualizar estado de la impresora","Refresh printer status"));
        refreshPrinterState.AccessibleName=Ui.T("Actualizar estado de la impresora","Refresh printer status");
    }

    /// <summary>Test-visible rendered status text, independent of an actual printer connection.</summary>
    internal string PrinterStatusText=>printerState.Text;

    /// <summary>Stops polling; outstanding native reads clean their own handles when Windows returns.</summary>
    private void DisposePrinterStatus()
    {
        printerStateDisposed=true;printerStateTimer.Stop();printerStateTimer.Dispose();
    }
}
