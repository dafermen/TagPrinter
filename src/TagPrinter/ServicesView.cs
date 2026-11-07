using System.Data;
using System.Globalization;
namespace TagPrinter;

/// <summary>Local service workspace with two clear lists and an optional stable per-asset scope.</summary>
internal sealed class ServicesView : UserControl
{
    private readonly HistoryStore history;
    private readonly TabControl tabs=new(){Dock=DockStyle.Fill};
    private readonly TabPage maintenanceTab=new(){BackColor=Color.White},warrantyTab=new(){BackColor=Color.White};
    private readonly Label assetLabel=new(){AutoSize=true,MaximumSize=new Size(780,0),Padding=new Padding(4,8,4,0)};
    private readonly Button all=new(){AutoSize=true};
    private readonly ServiceListView maintenance,warranties;
    private string? assetUid;
    /// <summary>Builds lazy list views; hidden lists do not issue database queries.</summary>
    internal ServicesView(HistoryStore history,Action<string> openAsset)
    {
        this.history=history;Dock=DockStyle.Fill;BackColor=Color.White;var header=new FlowLayoutPanel{Dock=DockStyle.Top,Height=66,Padding=new Padding(12,4,8,0),WrapContents=true};header.Controls.AddRange([all,assetLabel]);all.Click+=(_,_)=>ShowAsset(null);
        maintenance=new ServiceListView(history,"Maintenance",ShowAsset,openAsset);warranties=new ServiceListView(history,"Warranty",ShowAsset,openAsset);maintenanceTab.Controls.Add(maintenance);warrantyTab.Controls.Add(warranties);tabs.TabPages.AddRange([maintenanceTab,warrantyTab]);tabs.SelectedIndexChanged+=(_,_)=>RefreshData();Controls.Add(tabs);Controls.Add(header);RefreshLanguage();
    }
    /// <summary>Preserves the selected subtab, refreshes localized controls, and leaves database text untouched.</summary>
    internal void RefreshLanguage(){maintenanceTab.Text=ServiceStore.Caption("Maintenance");warrantyTab.Text=ServiceStore.Caption("Warranty");all.Text=Ui.T("Todos los activos","All assets");RefreshScopeCaption();maintenance.RefreshLanguage();warranties.RefreshLanguage();FluentIcons.ApplyTree(this);}
    /// <summary>Restricts both lists to one immutable asset identity; null restores the global scope.</summary>
    internal void ShowAsset(string? uid){assetUid=uid;RefreshScopeCaption();maintenance.SetAsset(uid);warranties.SetAsset(uid);RefreshData();}
    /// <summary>Updates the current asset caption after edits/restores, including serial changes.</summary>
    private void RefreshScopeCaption(){var asset=assetUid is null?null:new AssetStore(history).Get(assetUid);assetLabel.Text=assetUid is null?Ui.T("Avisos locales al abrir o actualizar. No requieren impresora.","Local reminders on open or refresh. No printer required."):asset is null?Ui.T("El activo ya no está en esta base.","The asset is no longer in this database."):asset.Code+" · "+asset.AssetType+" · "+asset.Brand+" · "+asset.Model+" · "+asset.Serial;}
    /// <summary>Refreshes only the selected list. Dates are recalculated rather than persisted as an overdue status.</summary>
    internal void RefreshData(){RefreshScopeCaption();if(tabs.SelectedTab==warrantyTab)warranties.RefreshData();else maintenance.RefreshData();}
    /// <summary>Exposes the real selected list to isolated UI tests.</summary>
    internal ServiceListView List(bool warranty=false){tabs.SelectedTab=warranty?warrantyTab:maintenanceTab;return warranty?warranties:maintenance;}
}

/// <summary>Bounded service browser with date alerts, explicit edits, revision history and cancellable CSV export.</summary>
internal sealed class ServiceListView : UserControl
{
    private readonly HistoryStore history;
    private readonly ServiceStore store;
    private readonly string kind;
    private readonly Action<string?> focusAsset;
    private readonly Action<string> openAsset;
    private readonly TextBox search=new(){Width=250,MaxLength=250};
    private readonly ComboBox status=new(){Width=170,DropDownStyle=ComboBoxStyle.DropDownList,Tag="UserData"},deadline=new(){Width=190,DropDownStyle=ComboBoxStyle.DropDownList,Tag="UserData"};
    private readonly NumericUpDown days=new(){Minimum=1,Maximum=365,Value=30,Width=60};
    private readonly Label daysLabel=new(){AutoSize=true,Padding=new Padding(0,6,0,0)};
    private readonly CheckBox retired=new(){AutoSize=true};
    private readonly Label summary=new(){Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0)};
    private readonly Label pageLabel=new(){AutoSize=true,Padding=new Padding(4,6,4,0)};
    private readonly Button previous=new(){Text="◀",Width=42},next=new(){Text="▶",Width=42};
    private readonly List<(Button Button,string Es,string En)> buttons=[];
    private readonly BackgroundRead<ServicePage> reads=new();
    private DataTable? table;
    private string? assetUid;
    private int page;
    private string sort="Due";
    private bool descending,translating,pending;
    internal readonly DataGridView Grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,MultiSelect=false,RowHeadersVisible=false,BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.FullRowSelect};
    private sealed record Choice(string Key,string Caption){public override string ToString()=>Caption;}
    internal bool ForceAsyncReads {get=>reads.ForceAsync;set=>reads.ForceAsync=value;}
    /// <summary>Builds the real list. All commands resolve stable IDs; list selection never submits a printer job.</summary>
    internal ServiceListView(HistoryStore history,string kind,Action<string?> focusAsset,Action<string> openAsset)
    {
        this.history=history;store=new(history);this.kind=kind;this.focusAsset=focusAsset;this.openAsset=openAsset;Dock=DockStyle.Fill;Padding=new Padding(12);BackColor=Color.White;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1};root.ColumnStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,96));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,56));root.RowStyles.Add(new(SizeType.Absolute,90));var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};filters.Controls.AddRange([search,status,deadline,daysLabel,days,retired]);
        Button Add(FlowLayoutPanel parent,string es,string en,Action action){var b=new Button{AutoSize=true};buttons.Add((b,es,en));b.Click+=(_,_)=>Run(action);parent.Controls.Add(b);return b;}
        Add(filters,"Buscar / actualizar","Search / refresh",()=>{page=0;RefreshData();});Add(filters,"Limpiar filtros","Clear filters",()=>{translating=true;search.Clear();status.SelectedIndex=deadline.SelectedIndex=0;days.Value=30;retired.Checked=assetUid is not null;translating=false;page=0;RefreshData();});
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};Add(actions,kind=="Warranty"?"Nueva garantía":"Nuevo mantenimiento",kind=="Warranty"?"New warranty":"New maintenance",NewRecord);Add(actions,"Editar registro","Edit record",()=>Edit(Selected()));Add(actions,"Historial de cambios","Change history",ShowChanges);Add(actions,"Ver activo","View asset",()=>openAsset(Selected().AssetUid));Add(actions,"Exportar CSV…","Export CSV…",Export);previous.Click+=(_,_)=>{if(page>0){page--;RefreshData();}};next.Click+=(_,_)=>{page++;RefreshData();};actions.Controls.AddRange([previous,pageLabel,next]);
        root.Controls.Add(filters,0,0);root.Controls.Add(Grid,0,1);root.Controls.Add(summary,0,2);root.Controls.Add(actions,0,3);Controls.Add(root);
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;page=0;Run(RefreshData);}};
        status.SelectedIndexChanged+=(_,_)=>Changed();deadline.SelectedIndexChanged+=(_,_)=>Changed();days.ValueChanged+=(_,_)=>Changed();retired.CheckedChanged+=(_,_)=>Changed();
        Grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)Run(()=>Edit(Selected()));};Grid.ColumnHeaderMouseClick+=(_,e)=>{if(e.ColumnIndex<0)return;string key=Grid.Columns[e.ColumnIndex].Name;if(key is not ("Code" or "Asset" or "Serial" or "Title" or "Status" or "Provider" or "Due"))return;descending=sort==key?!descending:false;sort=key;page=0;RefreshData();};
        HandleCreated+=(_,_)=>{if(pending && Visible)RefreshData();};VisibleChanged+=(_,_)=>{if(Visible)RefreshData();};RefreshLanguage();
    }
    /// <summary>Restarts paging only for explicit filter changes; translations preserve the stored keys.</summary>
    private void Changed(){if(!translating){page=0;Run(RefreshData);}}
    /// <summary>Captures all active filter values before starting a worker.</summary>
    internal ServiceFilter Filter=>new(kind,search.Text,(status.SelectedItem as Choice)?.Key??"",(deadline.SelectedItem as Choice)?.Key??"All",(int)days.Value,assetUid,retired.Checked);
    /// <summary>Switches scope explicitly and clears stale text/state filters so a newly created record can be found.</summary>
    internal void SetAsset(string? uid){assetUid=uid;translating=true;search.Clear();status.SelectedIndex=deadline.SelectedIndex=0;retired.Checked=uid is not null;translating=false;page=0;RefreshData();}
    /// <summary>Translates captions and choice presentation without translating user data or stable SQL keys.</summary>
    internal void RefreshLanguage()
    {
        translating=true;string state=(status.SelectedItem as Choice)?.Key??"",date=(deadline.SelectedItem as Choice)?.Key??"All";status.Items.Clear();status.Items.Add(new Choice("",Ui.T("Todos los estados","All statuses")));foreach(string key in kind=="Warranty"?ServiceStore.WarrantyStates:ServiceStore.MaintenanceStates)status.Items.Add(new Choice(key,ServiceStore.Caption(key)));status.SelectedIndex=Math.Max(0,status.Items.Cast<Choice>().ToList().FindIndex(c=>c.Key==state));deadline.Items.Clear();foreach(string key in new[]{"All","Attention","Late","Upcoming"})deadline.Items.Add(new Choice(key,ServiceStore.Caption(key)));deadline.SelectedIndex=Math.Max(0,deadline.Items.Cast<Choice>().ToList().FindIndex(c=>c.Key==date));daysLabel.Text=Ui.T("Avisar en días","Look ahead days");retired.Text=Ui.T("Incluir activos dados de baja","Include retired assets");search.PlaceholderText=Ui.T("Serial, activo, descripción o proveedor…","Serial, asset, description or provider…");foreach(var b in buttons)b.Button.Text=Ui.T(b.Es,b.En);translating=false;FluentIcons.ApplyTree(this);RefreshData();
    }
    /// <summary>Issues one latest-only cancellable request. Hidden tabs defer work until selected.</summary>
    internal void RefreshData()
    {
        if(!IsHandleCreated || !Visible){pending=true;return;}pending=false;var filter=Filter;int requested=page;string key=sort;bool reverse=descending;var today=DateOnly.FromDateTime(DateTime.Today);
        reads.Submit(token=>store.ReadPage(filter,requested,50,key,reverse,today,token),result=>Bind(result,today,filter.Days),failure=>{Grid.DataSource=null;summary.Text=failure.Message;},busy=>{Grid.Enabled=!busy;if(busy){previous.Enabled=next.Enabled=false;summary.Text=Ui.T("Cargando registros…","Loading records…");}});
    }
    /// <summary>Builds only one page of display values; no full service registry is retained by the grid.</summary>
    private void Bind(ServicePage result,DateOnly today,int lookAhead)
    {
        page=result.Page;var data=new DataTable();foreach(string key in new[]{"Id","Code","Asset","Serial","Description","Title","Due","Alert","Status","Category","Provider","Cost","Currency","Start","Completed","AssetStatus"})data.Columns.Add(key);foreach(var row in result.Rows){var s=row.Record;data.Rows.Add(s.Id,s.Code,row.AssetCode,row.Serial,row.AssetType+" · "+row.Brand+" · "+row.Model,s.Title,s.DueDate,ServiceStore.Alert(s,today,lookAhead),ServiceStore.Caption(s.Status),ServiceStore.Caption(s.Category),s.Provider,s.Cost?.ToString("N2",CultureInfo.CurrentCulture)??"",s.Currency,s.StartDate,s.CompletedDate,AssetStore.StatusCaption(row.AssetStatus));}
        var old=table;table=data;Grid.DataSource=data;old?.Dispose();Grid.Columns["Id"].Visible=false;Grid.Columns["Code"].Width=100;Grid.Columns["Asset"].Width=100;Grid.Columns["Title"].Width=180;Grid.Columns["Description"].Width=230;Grid.Columns["Status"].Width=130;Grid.Columns["Alert"].Width=110;Grid.Columns["Serial"].Width=120;Grid.Columns["Due"].Width=95;
        string[] display=["Code","Asset","Serial","Title","Due","Alert","Status","Category","Provider","Cost","Currency","Start","Completed","AssetStatus","Description"];for(int i=0;i<display.Length;i++)Grid.Columns[display[i]].DisplayIndex=i+1;
        var labels=new[]{"Id",Ui.T("Registro","Record"),Ui.T("Activo","Asset"),"Serial",Ui.T("Tipo / marca / modelo","Type / brand / model"),Ui.T("Descripción","Description"),kind=="Warranty"?Ui.T("Vence","Expires"):Ui.T("Programado","Scheduled"),Ui.T("Aviso","Reminder"),Ui.T("Estado","Status"),Ui.T("Tipo","Type"),Ui.T("Proveedor","Provider"),Ui.T("Costo","Cost"),Ui.T("Moneda","Currency"),Ui.T("Inicio","Start"),Ui.T("Finalizado","Completed"),Ui.T("Estado del activo","Asset state")};for(int i=0;i<labels.Length;i++){Grid.Columns[i].HeaderText=labels[i];Grid.Columns[i].SortMode=DataGridViewColumnSortMode.Programmatic;Grid.Columns[i].HeaderCell.SortGlyphDirection=Grid.Columns[i].Name==sort?(descending?SortOrder.Descending:SortOrder.Ascending):SortOrder.None;}
        foreach(string name in new[]{"Category","Cost","Currency","Completed"})Grid.Columns[name].Visible=kind!="Warranty";
        for(int i=0;i<result.Rows.Count;i++){var s=result.Rows[i].Record;if(s.Status is not ("Completed" or "Canceled" or "Closed") && string.CompareOrdinal(s.DueDate,today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))<0)Grid.Rows[i].DefaultCellStyle.ForeColor=Color.Firebrick;}
        pageLabel.Text=$"{page+1} / {Math.Max(1,(result.Total+49)/50)} · {result.Total}";previous.Enabled=page>0;next.Enabled=(page+1)*50<result.Total;
        summary.Text=string.Format(Ui.T("{0:N0} registros · {1:N0} atrasados/vencidos · {2:N0} vencen en {3} días.\nAvisos según búsqueda, estado y activo; incluyen todas las fechas. Exportar CSV incluye todas las páginas filtradas.","{0:N0} records · {1:N0} overdue/expired · {2:N0} due within {3} days.\nReminders follow search, status and asset scope across all dates. CSV includes every filtered page."),result.Total,result.Late,result.Upcoming,lookAhead);
    }
    /// <summary>Resolves a fresh revision by stable ID after checking that no obsolete read is pending.</summary>
    private ServiceRecord Selected(){if(reads.IsBusy || Grid.CurrentRow is null)throw new InvalidOperationException(Ui.T("Espera y selecciona un registro.","Wait and select a record."));return store.Get(Convert.ToInt64(Grid.CurrentRow.Cells["Id"].Value))??throw new InvalidDataException("Service record not found.");}
    /// <summary>Uses a paged picker globally; asset-scoped entry requires no second selection.</summary>
    private void NewRecord()
    {
        var asset=assetUid is null?null:new AssetStore(history).Get(assetUid);if(assetUid is null){using var picker=new AssetPicker(history);if(picker.ShowDialog(FindForm())!=DialogResult.OK)return;asset=picker.Selected;}if(asset is null)throw new InvalidOperationException(Ui.T("Crea o selecciona un activo primero.","Create or select an asset first."));
        using var editor=new ServiceEditor(history,ServiceEditor.Draft(kind,asset.Uid),asset);if(editor.ShowDialog(FindForm())==DialogResult.OK)focusAsset(asset.Uid);
    }
    /// <summary>Edits explicitly. Canceling the dialog leaves both record and asset unchanged.</summary>
    private void Edit(ServiceRecord record){var asset=new AssetStore(history).Get(record.AssetUid)??throw new InvalidDataException("Asset not found.");using var editor=new ServiceEditor(history,record,asset);if(editor.ShowDialog(FindForm())==DialogResult.OK)RefreshData();}
    /// <summary>Shows immutable field-by-field history through a bounded revision reader.</summary>
    private void ShowChanges(){var record=Selected();using var dialog=new AppWindow{Text=record.Code+" · "+Ui.T("Historial de cambios","Change history"),Width=1100,Height=600,StartPosition=FormStartPosition.CenterParent};dialog.Controls.Add(new ServiceChangesView(store,record.Id));dialog.ShowDialog(FindForm());}
    /// <summary>Streams the captured filter into a reviewed destination; export does not create inventory or printer events.</summary>
    private void Export(){using var file=new SaveFileDialog{Filter="CSV|*.csv",DefaultExt="csv",AddExtension=true,FileName=kind=="Warranty"?"TagPrinter-warranties.csv":"TagPrinter-maintenance.csv"};if(file.ShowDialog(FindForm())!=DialogResult.OK)return;var filter=Filter;string path=file.FileName;ExportProgressDialog.Run(FindForm(),(progress,token)=>store.ExportCsv(path,filter,token,progress));}
    /// <summary>Handles recoverable command errors, while tests fail without opening unattended message boxes.</summary>
    private void Run(Action action){try{action();}catch(Exception failure){if(Environment.GetCommandLineArgs().Contains("--self-test"))throw;MessageBox.Show(FindForm(),failure.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    /// <summary>Stops delivery before disposing child controls and releases the owned display table.</summary>
    /// <param name="disposing">True for explicit managed disposal.</param>
    protected override void Dispose(bool disposing){if(disposing){reads.Dispose();table?.Dispose();}base.Dispose(disposing);}
}
