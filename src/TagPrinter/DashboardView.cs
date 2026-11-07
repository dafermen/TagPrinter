using System.Data;
namespace TagPrinter;

/// <summary>Clickable canonical counts and groups; all drill-downs retain exact search/date/state scope.</summary>
public sealed class DashboardView : UserControl
{
    private readonly HistoryStore history;
    private readonly Action<AssetFilter> open;
    private readonly TextBox search=new(){Width=210};
    private readonly ComboBox grouping=new(){Width=175,DropDownStyle=ComboBoxStyle.DropDownList,Tag="UserData"};
    private readonly DateTimePicker from=new(){ShowCheckBox=true,Checked=false,Format=DateTimePickerFormat.Short,Width=125},to=new(){ShowCheckBox=true,Checked=false,Format=DateTimePickerFormat.Short,Width=125};
    private readonly CheckBox includeRetired=new(){AutoSize=true};
    private readonly Button refresh=new(){AutoSize=true};
    private readonly Label searchLabel=new(){AutoSize=true},fromLabel=new(){AutoSize=true},toLabel=new(){AutoSize=true},groupLabel=new(){AutoSize=true};
    private readonly Label summary=new(){Dock=DockStyle.Fill},help=new(){Dock=DockStyle.Fill};
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,RowHeadersVisible=false};
    private readonly Dictionary<string,Button> cards=[];
    private DataTable? table;
    private List<AssetGroup> groups=[];
    private bool translating,refreshPending;
    private readonly BackgroundRead<DashboardPage> reads=new();
    private readonly Button previous=new(){Text="◀",Width=42},next=new(){Text="▶",Width=42};
    private readonly Label pages=new(){AutoSize=true,Padding=new Padding(4,6,4,0)};
    private int groupPage;
    internal bool ForceAsyncReads {get=>reads.ForceAsync;set=>reads.ForceAsync=value;}
    private sealed record Choice(string Key,string Caption){public override string ToString()=>Caption;}
    /// <summary>Builds a Dashboard with the historical description-group navigation callback.</summary>
    /// <param name="history">Shared inventory database; tests supply isolated paths.</param>
    /// <param name="onOpen">Compatibility callback for description groups.</param>
    public DashboardView(HistoryStore history,Action<string,string,string> onOpen):this(history,f=>onOpen(f.Type??"",f.Brand??"",f.Model??"")){}
    /// <summary>Builds exact scoped navigation, state cards and flexible grouping without generating labels.</summary>
    internal DashboardView(HistoryStore history,Action<AssetFilter> onOpen)
    {
        this.history=history;open=onOpen;BackColor=Color.White;Dock=DockStyle.Fill;Padding=new Padding(16);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};root.ColumnStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.AutoSize));root.RowStyles.Add(new(SizeType.AutoSize));root.RowStyles.Add(new(SizeType.Absolute,70));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,90));
        var filters=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};filters.Controls.AddRange([searchLabel,search,fromLabel,from,toLabel,to,refresh,includeRetired,groupLabel,grouping]);
        var cardRow=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
        foreach(string key in new[]{"", "Available","Assigned","Repair","Unspecified","Retired"})
        {
            var button=new Button{Width=155,Height=56,BackColor=key=="Unspecified"?Color.FromArgb(255,244,214):Color.FromArgb(237,242,248),FlatStyle=FlatStyle.Flat};button.Click+=(_,_)=>OpenStatus(key);cards.Add(key,button);cardRow.Controls.Add(button);
        }
        root.Controls.Add(filters,0,0);root.Controls.Add(cardRow,0,1);root.Controls.Add(summary,0,2);root.Controls.Add(grid,0,3);var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};footer.RowStyles.Add(new(SizeType.Absolute,36));footer.RowStyles.Add(new(SizeType.Percent,100));var pager=new FlowLayoutPanel{Dock=DockStyle.Fill};pager.Controls.AddRange([previous,pages,next]);footer.Controls.Add(pager,0,0);footer.Controls.Add(help,0,1);root.Controls.Add(footer,0,4);Controls.Add(root);
        previous.Click+=(_,_)=>{if(groupPage>0){groupPage--;RefreshData();}};next.Click+=(_,_)=>{groupPage++;RefreshData();};
        HandleCreated+=(_,_)=>{if(refreshPending && Visible)RefreshData();};VisibleChanged+=(_,_)=>{if(Visible && refreshPending)RefreshData();};
        refresh.Click+=(_,_)=>Run(()=>{groupPage=0;RefreshData();});search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;groupPage=0;Run(RefreshData);}};includeRetired.CheckedChanged+=(_,_)=>{if(!translating){groupPage=0;Run(RefreshData);}};grouping.SelectedIndexChanged+=(_,_)=>{if(!translating){groupPage=0;Run(RefreshData);}};
        grid.CellContentClick+=(_,e)=>{if(e.RowIndex>=0 && e.ColumnIndex>=0 && grid.Columns[e.ColumnIndex].Name=="Quantity")Run(()=>OpenGroup(Convert.ToInt32(grid.Rows[e.RowIndex].Cells["GroupIndex"].Value)));};
        grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)Run(()=>OpenGroup(Convert.ToInt32(grid.Rows[e.RowIndex].Cells["GroupIndex"].Value)));};from.Checked=to.Checked=false;RefreshLanguage();
    }
    /// <summary>Builds the exact current scope used by both the displayed counts and destination list.</summary>
    internal AssetFilter CurrentScope
    {
        get
        {
            if(from.Checked && to.Checked && from.Value.Date>to.Value.Date)throw new ArgumentException(Ui.T("Revisa las fechas.","Check the date range."));
            return new(Search:search.Text.Trim(),FromUtc:from.Checked?new DateTimeOffset(from.Value.Date).ToUniversalTime():null,ToUtc:to.Checked?new DateTimeOffset(to.Value.Date.AddDays(1)).ToUniversalTime():null,ExcludeRetired:!includeRetired.Checked);
        }
    }
    /// <summary>Refreshes a translated selector without translating user values or enabling optional date filters.</summary>
    public void RefreshLanguage()
    {
        translating=true;string key=(grouping.SelectedItem as Choice)?.Key??"Description";
        searchLabel.Text=Ui.T("Buscar","Search");fromLabel.Text=Ui.T("Actualizados desde","Updated from");toLabel.Text=Ui.T("Hasta","To");groupLabel.Text=Ui.T("Agrupar por","Group by");refresh.Text=Ui.T("Buscar / actualizar","Search / refresh");search.PlaceholderText=Ui.T("Buscar activo…","Search assets…");from.AccessibleName=Ui.T("Desde","From");to.AccessibleName=Ui.T("Hasta","To");includeRetired.Text=Ui.T("Incluir dados de baja","Include retired");
        grouping.Items.Clear();grouping.Items.AddRange([new Choice("Description",Ui.T("Tipo / marca / modelo","Type / brand / model")),new Choice("Location",Ui.T("Ubicación","Location")),new Choice("Custodian",Ui.T("Responsable","Custodian"))]);grouping.SelectedIndex=Math.Max(0,grouping.Items.Cast<Choice>().ToList().FindIndex(c=>c.Key==key));translating=false;
        foreach(var card in cards)card.Value.Text=(card.Key.Length==0?Ui.T("Activos","Assets"):AssetStore.StatusCaption(card.Key))+Environment.NewLine+"…";
        help.Text=Ui.T("Pulsa una tarjeta o cantidad para abrir los activos con los mismos filtros. Las fechas filtran su última modificación.","Click a card or quantity to open assets with the same filters. Dates filter their last administrative update.");FluentIcons.ApplyTree(this);RefreshData();
    }
    /// <summary>Retained legacy helper for tests of earlier snapshots, not the current aggregation algorithm.</summary>
    /// <param name="events">Candidate historical events.</param>
    /// <returns>Latest event for each nonblank normalized serial.</returns>
    internal static List<HistoryEvent> UniqueAssets(IEnumerable<HistoryEvent> events)=>events.Where(e=>!string.IsNullOrWhiteSpace(e.State.Serial)).OrderByDescending(e=>e.Id).GroupBy(e=>e.State.Serial.Trim(),StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
    /// <summary>Aggregates in SQL, rebinds a small grouping table and maintains index-based identity through display sorting.</summary>
    public void RefreshData()
    {
        if(!reads.Synchronous && (!IsHandleCreated || !Visible)){refreshPending=true;return;}refreshPending=false;
        var filter=CurrentScope;string group=(grouping.SelectedItem as Choice)?.Key??"Description";int page=groupPage;
        reads.Submit(token=>new AssetStore(history).ReadDashboard(filter,group,page,50,token),result=>BindPage(result,group),error=>{grid.DataSource=null;summary.Text=error.Message;},busy=>{grid.Enabled=!busy;foreach(var card in cards.Values)if(busy)card.Enabled=false;if(busy){previous.Enabled=next.Enabled=false;summary.Text=Ui.T("Calculando resumen…","Calculating overview…");}});
    }
    /// <summary>Materializes only the current group page; cards use the full-scope SQL state totals.</summary>
    private void BindPage(DashboardPage result,string group)
    {
        groups=result.Groups;groupPage=result.Page;pages.Text=$"{groupPage+1} / {Math.Max(1,(result.TotalGroups+49)/50)} · {result.TotalGroups} "+Ui.T("grupos","groups");previous.Enabled=groupPage>0;next.Enabled=(groupPage+1)*50<result.TotalGroups;
        var data=new DataTable();data.Columns.Add("GroupIndex",typeof(int));
        string[] columns=group=="Description"?["Type","Brand","Model"]:[group];foreach(string column in columns)data.Columns.Add(column);data.Columns.Add("Quantity",typeof(long));
        for(int i=0;i<groups.Count;i++){var row=groups[i];if(group=="Description")data.Rows.Add(i,row.Type,row.Brand,row.Model,row.Count);else{string value=group=="Location"?row.Location:row.Custodian;data.Rows.Add(i,value.Length==0?Ui.T("Sin indicar","Not set"):value,row.Count);}}
        grid.DataSource=null;grid.Columns.Clear();grid.Columns.Add(new DataGridViewTextBoxColumn{Name="GroupIndex",DataPropertyName="GroupIndex",Visible=false});
        foreach(string column in columns)grid.Columns.Add(new DataGridViewTextBoxColumn{Name=column,DataPropertyName=column,HeaderText=column=="Type"?AssetCsv.Caption("AssetType"):AssetCsv.Caption(column),SortMode=DataGridViewColumnSortMode.Automatic});
        grid.Columns.Add(new DataGridViewLinkColumn{Name="Quantity",DataPropertyName="Quantity",HeaderText=Ui.T("Cantidad","Quantity"),TrackVisitedState=false,SortMode=DataGridViewColumnSortMode.Automatic});var old=table;table=data;grid.DataSource=data;old?.Dispose();
        foreach(var pair in cards){long number=pair.Key.Length==0?result.States.Values.Sum():result.States.GetValueOrDefault(pair.Key);pair.Value.Text=(pair.Key.Length==0?Ui.T("Activos","Assets"):AssetStore.StatusCaption(pair.Key))+Environment.NewLine+number;pair.Value.Enabled=number>0;pair.Value.AccessibleName=pair.Value.Text;}
        FluentIcons.ApplyTree(this);var labels=(Exports:result.Exports,Jobs:result.Jobs,Copies:result.Copies);var backup=BackupStatus.Read(history.DatabasePath);string when=DateTimeOffset.TryParse(backup.LastSuccessUtc,out var instant)?instant.LocalDateTime.ToString("yyyy-MM-dd HH:mm"):Ui.T("Aún no registrado","Not recorded yet");
        summary.Text=string.Format(Ui.T("Historial global de etiquetas: {0} exportaciones · {1} trabajos enviados · {2} copias solicitadas.","Global label log: {0} exports · {1} submitted jobs · {2} requested copies."),labels.Exports,labels.Jobs,labels.Copies)+Environment.NewLine+Ui.T("Último respaldo: ","Last backup: ")+when+(backup.Error.Length>0 || (backup.LastPath.Length>0 && !File.Exists(backup.LastPath))?Ui.T(" · Atención: revisa el estado del respaldo en Configuración."," · Attention: check backup status in Settings."):"");
    }
    /// <summary>Opens exactly the state behind a visible card; no reprint happens on navigation.</summary>
    internal void OpenStatus(string state){if(!reads.IsBusy)Run(()=>open(CurrentScope with {Status=state}));}
    /// <summary>Changes grouping explicitly; useful for isolated integration checks.</summary>
    internal void SetGrouping(string key){grouping.SelectedIndex=grouping.Items.Cast<Choice>().ToList().FindIndex(c=>c.Key==key);}
    /// <summary>Opens a stored raw group, including empty location/custodian and current date/search restrictions.</summary>
    internal void OpenGroup(int index)
    {
        if(reads.IsBusy)return;var row=groups[index];var filter=CurrentScope;string group=(grouping.SelectedItem as Choice)?.Key??"Description";
        open(group switch{"Location"=>filter with {Location=row.Location},"Custodian"=>filter with {Custodian=row.Custodian},_=>filter with {Type=row.Type,Brand=row.Brand,Model=row.Model}});
    }
    /// <summary>Shows readable query errors; verification must propagate failures instead of blocking on a modal warning.</summary>
    internal IReadOnlyList<AssetGroup> GroupRows=>groups;
    /// <summary>Sets real filter controls for integration tests and future scoped navigation.</summary>
    internal void SetFilters(string query,DateTime? start=null,DateTime? end=null,bool retired=false)
    {
        translating=true;search.Text=query;from.Checked=start.HasValue;if(start is {} a)from.Value=a;to.Checked=end.HasValue;if(end is {} b)to.Value=b;includeRetired.Checked=retired;translating=false;groupPage=0;RefreshData();
    }
    /// <summary>Moves to a clamped aggregate page without materializing other groups.</summary>
    internal void GoToGroupPage(int page){groupPage=Math.Max(0,page);RefreshData();}
    private void Run(Action action){try{action();}catch(Exception error){if(Environment.GetCommandLineArgs().Contains("--self-test"))throw;MessageBox.Show(FindForm(),error.Message,"TagPrinter");}}
    /// <summary>Releases the owned projection.</summary>
    /// <param name="disposing">True for explicit managed disposal.</param>
    protected override void Dispose(bool disposing){if(disposing){reads.Dispose();table?.Dispose();}base.Dispose(disposing);}
}
