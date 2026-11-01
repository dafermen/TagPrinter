namespace TagPrinter;

public partial class MainForm
{
    readonly HistoryStore history;
    readonly TabControl workspaceTabs=new(){Dock=DockStyle.Fill};
    readonly TabPage labelTab=new(), reportsTab=new();
    readonly ToolTip aboutTip=new();
    readonly Button aboutButton=new(){Width=42,Height=42,FlatStyle=FlatStyle.Flat,BackColor=Color.White};
    ReportsView? reports;
    long? sourceHistoryId;
    internal HistoryStore History=>history;
    internal string AboutTooltip=>aboutTip.GetToolTip(aboutButton)??"";
    internal ReportsView Reports=>reports!;
    internal bool ShowingLabel=>workspaceTabs.SelectedTab==labelTab;
    void InitializeWorkspace(Control labelContent)
    {
        labelTab.BackColor=Color.White;reportsTab.BackColor=Color.White;
        labelTab.Controls.Add(labelContent);workspaceTabs.TabPages.AddRange([labelTab,reportsTab]);
        reports=new ReportsView(history,LoadHistoryEvent);reportsTab.Controls.Add(reports);
        workspaceTabs.SelectedIndexChanged+=(_,_)=>{if(ready && workspaceTabs.SelectedTab==reportsTab)Guard(()=>reports.RefreshData());};
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
    void UpdateChrome()
    {
        labelTab.Text=Ui.T("Etiquetas","Labels");reportsTab.Text=Ui.T("Reportes","Reports");
        string about=Ui.T("Acerca de","About");aboutTip.SetToolTip(aboutButton,about);aboutButton.AccessibleName=about;
        reports?.RefreshLanguage();
    }
    WorkState GenerationState()=>CaptureWork() with
    {
        AssetType=assetType.Text.Trim(),Brand=brand.Text.Trim(),Model=model.Text.Trim(),Serial=serial.Text.Trim(),PartNumber=partNumber.Text.Trim(),MacAddress=macAddress.Text.Trim()
    };
    void RecordGeneration(string action,string path,WorkState snapshot)
    {
        try{history.Add(action,snapshot,path,sourceHistoryId);}
        catch(Exception ex)
        {
            MessageBox.Show(this,Ui.T("La operación terminó, pero no se pudo guardar el historial. No repitas una impresión automáticamente.\n","The operation completed, but its history could not be saved. Do not automatically repeat a print job.\n")+ex.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }
    internal void ExportTagFile(string path,bool zpl=false)
    {
        var snapshot=GenerationState();using var image=LabelRenderer.Render(Data());
        if(zpl)File.WriteAllText(path,LabelRenderer.ToZpl(image,(int)copies.Value),System.Text.Encoding.ASCII);
        else image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
        RecordGeneration(zpl?"ExportZpl":"ExportTag",path,snapshot);SaveConfig();
    }
    internal void ExportCodeFile(string path)
    {
        var snapshot=GenerationState();using var image=CodeRenderer.Render(snapshot.Serial,snapshot.Code==1,int.Parse(dpi.Text));
        image.Save(path,System.Drawing.Imaging.ImageFormat.Png);RecordGeneration("ExportCode",path,snapshot);SaveConfig();
    }
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
    internal void ShowReports(){workspaceTabs.SelectedTab=reportsTab;reports!.RefreshData();}
}
