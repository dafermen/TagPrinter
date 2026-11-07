using System.Globalization;
namespace TagPrinter;

/// <summary>Explicit service editor. Validation errors stay beside the form and never discard entered values.</summary>
internal sealed class ServiceEditor : AppWindow
{
    private readonly ServiceStore store;
    private readonly ServiceRecord original;
    private readonly TextBox title=new(){Name="ServiceTitle",MaxLength=160,Dock=DockStyle.Top};
    private readonly TextBox provider=new(){Name="ServiceProvider",MaxLength=160,Dock=DockStyle.Top};
    private readonly TextBox reference=new(){Name="ServiceReference",MaxLength=120,Dock=DockStyle.Top};
    private readonly TextBox notes=new(){Name="ServiceNotes",MaxLength=4000,Multiline=true,Height=100,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Top};
    private readonly ComboBox state=new(){Name="ServiceStatus",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,Tag="UserData"};
    private readonly ComboBox category=new(){Name="ServiceCategory",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,Tag="UserData"};
    private readonly DateTimePicker start=DateInput("ServiceStart"),due=DateInput("ServiceDue"),completed=DateInput("ServiceCompleted");
    private readonly CheckBox hasCost=new(){Name="ServiceHasCost",AutoSize=true};
    private readonly NumericUpDown cost=new(){Name="ServiceCost",DecimalPlaces=2,Minimum=0,Maximum=999999999.99m,Width=150,ThousandsSeparator=true};
    private readonly TextBox currency=new(){Name="ServiceCurrency",MaxLength=3,Width=65,CharacterCasing=CharacterCasing.Upper};
    private readonly Label error=new(){Name="ServiceError",Dock=DockStyle.Top,AutoSize=true,ForeColor=Color.Firebrick,MaximumSize=new Size(600,0)};
    internal ServiceRecord? Saved {get;private set;}
    /// <summary>One language-neutral choice key; its caption is translated only when constructing the dialog.</summary>
    private sealed record Choice(string Key){public override string ToString()=>ServiceStore.Caption(Key);}
    /// <summary>Builds a blank record without copying another job's dates, cost or identifiers.</summary>
    internal static ServiceRecord Draft(string kind,string assetUid)=>new(){AssetUid=assetUid,Kind=kind,Status=kind=="Warranty"?"Active":"Planned",Category=kind=="Warranty"?"":"Preventive",StartDate=kind=="Warranty"?DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):"",DueDate=(kind=="Warranty"?DateTime.Today.AddYears(1):DateTime.Today).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)};
    /// <summary>Uses a fixed date presentation while storing ISO calendar dates, never locale-dependent strings.</summary>
    private static DateTimePicker DateInput(string name)=>new(){Name=name,Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",MinDate=new DateTime(1900,1,1),MaxDate=new DateTime(9998,12,31),Dock=DockStyle.Top};
    /// <summary>Edits one immutable kind/asset identity. No save occurs until the user presses Save.</summary>
    internal ServiceEditor(HistoryStore history,ServiceRecord source,AssetRecord asset)
    {
        store=new(history);original=source;bool warranty=source.Kind=="Warranty";Text=(source.Id==0?Ui.T("Nuevo registro · ","New record · "):source.Code+" · ")+ServiceStore.Caption(source.Kind);Width=720;Height=warranty?630:790;MinimumSize=new Size(570,560);StartPosition=FormStartPosition.CenterParent;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,AutoScroll=true,Padding=new Padding(16)};root.ColumnStyles.Add(new(SizeType.Absolute,180));root.ColumnStyles.Add(new(SizeType.Percent,100));int row=0;
        var identity=new Label{Text=asset.Code+" · "+asset.AssetType+" · "+asset.Brand+" · "+asset.Model+" · "+asset.Serial,AutoSize=true,MaximumSize=new Size(620,0),Padding=new Padding(0,0,0,12)};root.Controls.Add(identity,0,row++);root.SetColumnSpan(identity,2);root.RowStyles.Add(new(SizeType.AutoSize));
        void Field(string label,Control input){root.RowStyles.Add(new(SizeType.AutoSize));root.Controls.Add(new Label{Text=label,AutoSize=true,MaximumSize=new Size(170,0),Padding=new Padding(0,6,0,6)},0,row);root.Controls.Add(input,1,row++);}
        title.Text=source.Title;provider.Text=source.Provider;reference.Text=source.Reference;notes.Text=source.Notes;
        foreach(string key in warranty?ServiceStore.WarrantyStates:ServiceStore.MaintenanceStates)state.Items.Add(new Choice(key));state.SelectedIndex=Math.Max(0,state.Items.Cast<Choice>().ToList().FindIndex(c=>c.Key==source.Status));
        Field(Ui.T("Descripción *","Description *"),title);Field(Ui.T("Estado","Status"),state);
        if(!warranty){category.Items.AddRange([new Choice("Preventive"),new Choice("Corrective")]);category.SelectedIndex=source.Category=="Corrective"?1:0;Field(Ui.T("Tipo de mantenimiento","Maintenance type"),category);}
        void DateValue(DateTimePicker control,string value){if(value.Length>0)control.Value=DateTime.ParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture);}
        start.ShowCheckBox=!warranty;DateValue(start,source.StartDate);start.Checked=source.StartDate.Length>0;DateValue(due,source.DueDate);DateValue(completed,source.CompletedDate);
        Field(warranty?Ui.T("Inicio de garantía *","Warranty start *"):Ui.T("Inicio real (opcional)","Actual start (optional)"),start);Field(warranty?Ui.T("Vence (día incluido) *","Expires (inclusive) *"):Ui.T("Fecha programada *","Scheduled date *"),due);
        if(!warranty)
        {
            Field(Ui.T("Finalización real *","Actual completion *"),completed);void StatusChanged(){completed.Enabled=((Choice)state.SelectedItem!).Key=="Completed";}state.SelectedIndexChanged+=(_,_)=>StatusChanged();StatusChanged();
            hasCost.Text=Ui.T("Registrar costo","Record an amount");hasCost.Checked=source.Cost is not null;cost.Value=source.Cost??0;currency.Text=source.Currency;var money=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,WrapContents=true};money.Controls.AddRange([hasCost,cost,new Label{Text=Ui.T("Moneda","Currency"),AutoSize=true,Padding=new Padding(0,6,0,0)},currency]);Field(Ui.T("Costo registrado / estimado","Recorded / estimated cost"),money);void CostEnabled(){cost.Enabled=currency.Enabled=hasCost.Checked;}hasCost.CheckedChanged+=(_,_)=>CostEnabled();CostEnabled();
        }
        Field(Ui.T("Proveedor / fabricante","Provider / manufacturer"),provider);Field(Ui.T("Referencia / caso","Reference / case"),reference);Field(Ui.T("Notas / trabajo realizado","Notes / work performed"),notes);
        var hint=new Label{AutoSize=true,MaximumSize=new Size(620,0),Text=warranty?Ui.T("Los avisos se calculan por fechas. Registrar una reclamación no amplía la garantía.","Date-based reminders. Opening a claim does not extend the warranty."):Ui.T("El estado del activo se administra por separado en Activos. La moneda se guarda tal como la indiques.","Manage the asset state separately in Assets. Currency is recorded as entered."),ForeColor=Color.DimGray};root.Controls.Add(hint,0,row++);root.SetColumnSpan(hint,2);
        var footer=new Panel{Dock=DockStyle.Bottom,Height=100,Padding=new Padding(16,4,16,4)};var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=40};var save=new Button{Name="SaveService",AutoSize=true,Text=Ui.T("Guardar registro","Save record")};var cancel=new Button{AutoSize=true,Text=Ui.T("Cancelar","Cancel"),DialogResult=DialogResult.Cancel};buttons.Controls.AddRange([save,cancel]);footer.Controls.Add(error);footer.Controls.Add(buttons);Controls.Add(root);Controls.Add(footer);CancelButton=cancel;
        // Old validation feedback is cleared when input changes; the next Save performs full validation again.
        foreach(Control input in new Control[]{title,provider,reference,notes,state,category,start,due,completed,cost,currency})input.TextChanged+=(_,_)=>error.Text="";hasCost.CheckedChanged+=(_,_)=>error.Text="";
        root.SizeChanged+=(_,_)=>{int available=Math.Max(180,root.ClientSize.Width-40);identity.MaximumSize=hint.MaximumSize=new Size(available,0);error.MaximumSize=new Size(Math.Max(100,ClientSize.Width-40),0);};
        save.Click+=(_,_)=>TrySave();FluentIcons.ApplyTree(this);
    }
    /// <summary>Captures values and commits one revision. Validation/concurrency failures keep the editor open.</summary>
    internal bool TrySave()
    {
        try
        {
            bool warranty=original.Kind=="Warranty";string status=((Choice)state.SelectedItem!).Key;
            string Date(DateTimePicker control)=>control.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            Saved=store.Save(original with {Title=title.Text,Status=status,Category=warranty?"":((Choice)category.SelectedItem!).Key,StartDate=warranty||start.Checked?Date(start):"",DueDate=Date(due),CompletedDate=!warranty&&status=="Completed"?Date(completed):"",Provider=provider.Text,Reference=reference.Text,Notes=notes.Text,Cost=!warranty&&hasCost.Checked?cost.Value:null,Currency=!warranty&&hasCost.Checked?currency.Text:""});DialogResult=DialogResult.OK;return true;
        }
        catch(Exception failure){error.Text=failure.Message;return false;}
    }
}
