namespace TagPrinter;

/// <summary>
/// Coordinates the editable label, preview, autosave and output. Its partial History file integrates tabs
/// and event recording.
/// </summary>
public partial class MainForm
{
    // The form coordinates views; the store owns persistence. sourceHistoryId tracks the loaded source
    // event, not a unique asset.
    readonly HistoryStore history;
    // workspaceTabs hosts the editor and reporting views; labelTab/reportsTab/dashboardTab are stable
    // navigation targets.
    readonly TabControl workspaceTabs=new(){Dock=DockStyle.Fill};
    // Stable tab instances used to select editor, Reports and Dashboard.
    readonly TabPage labelTab=new(), reportsTab=new(), dashboardTab=new();
    // Views are assigned during initialization; null-conditional access allows captions to refresh before
    // all children exist.
    DashboardView? dashboard;
    // aboutTip owns translated hints. aboutButton owns a scaled copy of the embedded GitHub image.
    readonly ToolTip aboutTip=new();
    // Borderless project-link button owning its scaled embedded image.
    readonly Button aboutButton=new(){Width=42,Height=42,FlatStyle=FlatStyle.Flat,BackColor=Color.White};
    // Assigned during workspace creation; tests access it only after initialization.
    ReportsView? reports;
    // Null for fresh work; otherwise the loaded event ID is attached to the next generated event as provenance.
    long? sourceHistoryId;
    /// <summary>Internal access to the generation store for isolated tests.</summary>
    internal HistoryStore History=>history;
    /// <summary>Current About hint used by accessibility tests.</summary>
    internal string AboutTooltip=>aboutTip.GetToolTip(aboutButton)??"";
    /// <summary>Initialized Reports view exposed to integration tests.</summary>
    internal ReportsView Reports=>reports!;
    /// <summary>Whether history loading selected the editor tab.</summary>
    internal bool ShowingLabel=>workspaceTabs.SelectedTab==labelTab;
    /// <summary>Creates three tabs, refresh/load callbacks and footer project link.</summary>
    /// <param name="labelContent">Editor layout hosted by the Labels tab.</param>
    void InitializeWorkspace(Control labelContent)
    {
        labelTab.BackColor=Color.White;reportsTab.BackColor=Color.White;
        labelTab.Controls.Add(labelContent);workspaceTabs.TabPages.AddRange([labelTab,reportsTab,dashboardTab]);
        reports=new ReportsView(history,LoadHistoryEvent,()=>{if(sourceHistoryId is { } id && history.Get(id) is null)sourceHistoryId=null;});reportsTab.Controls.Add(reports);
        // Refresh on selection so recent output appears without continuous background polling.
        workspaceTabs.SelectedIndexChanged+=(_,_)=>{if(ready && workspaceTabs.SelectedTab==reportsTab)Guard(()=>reports.RefreshData());};
        dashboard=new DashboardView(history,(type,brand,model)=>{workspaceTabs.SelectedTab=reportsTab;reports.ShowGroup(type,brand,model);});dashboardTab.Controls.Add(dashboard);
        workspaceTabs.SelectedIndexChanged+=(_,_)=>{if(ready && workspaceTabs.SelectedTab==dashboardTab)Guard(()=>dashboard.RefreshData());};
        aboutButton.FlatAppearance.BorderSize=0;
        using(var stream=typeof(MainForm).Assembly.GetManifestResourceStream("TagPrinter.GitHub.png"))
        {
            if(stream is not null){using var original=Image.FromStream(stream);aboutButton.Image=new Bitmap(original,new Size(28,28));}
        }
        aboutButton.Click+=(_,_)=>Guard(()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ProjectUrl){UseShellExecute=true}));
        var footer=new TableLayoutPanel{Dock=DockStyle.Bottom,Height=66,ColumnCount=2,RowCount=1,Padding=new Padding(4,0,0,0)};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,52));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.Controls.Add(aboutButton,0,0);status.Dock=DockStyle.Fill;footer.Controls.Add(status,1,0);
        Controls.Add(workspaceTabs);Controls.Add(footer);UpdateChrome();
    }
    /// <summary>Updates tab captions, accessibility hints and child-view translations.</summary>
    void UpdateChrome()
    {
        labelTab.Text=Ui.T("Etiquetas","Labels");reportsTab.Text=Ui.T("Reportes","Reports");
        string about=Ui.T("Acerca de","About");aboutTip.SetToolTip(aboutButton,about);aboutButton.AccessibleName=about;
        foreach(var item in visibilityFields)
        {
            string hint=string.Format(Ui.T("Incluir {0} en la etiqueta","Include {0} on the label"),item.Caption.Text);
            item.Check.AccessibleName=hint;aboutTip.SetToolTip(item.Check,hint);
        }
        ShowPrinterStatus(lastPrinterState);
        reports?.RefreshLanguage();dashboardTab.Text="Dashboard";dashboard?.RefreshLanguage();
    }
    /// <summary>Captures saved work and trims asset values for a completed output event.</summary>
    /// <returns>Complete snapshot with trimmed asset text.</returns>
    WorkState GenerationState()=>CaptureWork() with
    {
        AssetType=assetType.Text.Trim(),Brand=brand.Text.Trim(),Model=model.Text.Trim(),Serial=serial.Text.Trim(),PartNumber=partNumber.Text.Trim(),MacAddress=macAddress.Text.Trim()
    };
    /// <summary>
    /// Records an already completed external output; SQLite failure warns without resubmitting a print or
    /// recreating output.
    /// </summary>
    /// <param name="action">Stable code describing the completed external output.</param>
    /// <param name="path">File path to read or write; test callers use their isolated verification directory.</param>
    /// <param name="snapshot">Trimmed saved-work state associated with completed output.</param>
    void RecordGeneration(string action,string path,WorkState snapshot)
    {
        // Output and SQLite are separate systems. A failed history insert must not automatically repeat a
        // print already submitted.
        try{history.Add(action,snapshot,path,sourceHistoryId);}
        catch(Exception ex)
        {
            MessageBox.Show(this,Ui.T("La operación terminó, pero no se pudo guardar el historial. No repitas una impresión automáticamente.\n","The operation completed, but its history could not be saved. Do not automatically repeat a print job.\n")+ex.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }
    /// <summary>
    /// Writes full-label PNG or raster ZPL, then records success and saves work. File errors occur before
    /// event insertion.
    /// </summary>
    /// <param name="path">File path to read or write; test callers use their isolated verification directory.</param>
    /// <param name="zpl">True writes a raster ZPL file; false writes PNG.</param>
    internal void ExportTagFile(string path,bool zpl=false)
    {
        var snapshot=GenerationState();using var image=LabelRenderer.Render(Data());
        if(zpl)File.WriteAllText(path,LabelRenderer.ToZpl(image,(int)copies.Value),System.Text.Encoding.ASCII);
        else image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
        RecordGeneration(zpl?"ExportZpl":"ExportTag",path,snapshot);SaveConfig();
    }
    /// <summary>
    /// Writes standalone code PNG and records it; serial remains mandatory when the full label hides its
    /// symbol.
    /// </summary>
    /// <param name="path">File path to read or write; test callers use their isolated verification directory.</param>
    internal void ExportCodeFile(string path)
    {
        var snapshot=GenerationState();using var image=CodeRenderer.Render(snapshot.Serial,snapshot.Code==1,int.Parse(dpi.Text),snapshot.CodePercent);
        image.Save(path,System.Drawing.Imaging.ImageFormat.Png);RecordGeneration("ExportCode",path,snapshot);SaveConfig();
    }
    /// <summary>
    /// Loads exact historical size as custom, retains current language and clears an unavailable printer;
    /// never prints automatically.
    /// </summary>
    /// <param name="row">Event with the original design and printer to restore.</param>
    internal void LoadHistoryEvent(HistoryEvent row)
    {
        // Keep exact historical dimensions even when the preset catalog has since changed.
        Restore(row.State with {LabelPresetId="custom",Language=Ui.Language});
        bool available=printers.Items.Contains(row.State.Printer);
        if(!available)printers.SelectedIndex=-1;
        sourceHistoryId=row.Id;assetSection.Expanded=true;workspaceTabs.SelectedTab=labelTab;
        UpdatePrintSummary();SaveConfig();
        status.Text=Ui.T("Etiqueta recuperada. Revisa los datos y pulsa Imprimir.","Label loaded. Review it and click Print.")+
            (available?"":Ui.T(" Selecciona una impresora disponible en 04."," Select an available printer in 04."));
    }
    /// <summary>Selects Reports and reloads current filtered rows; also used by tests.</summary>
    internal void ShowReports(){workspaceTabs.SelectedTab=reportsTab;reports!.RefreshData();}
}
