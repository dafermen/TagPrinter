using System.Data;
namespace TagPrinter;
/// <summary>
/// Groups canonical assets by type, brand and model and separately summarizes label activity. It is not verified physical
/// inventory.
/// </summary>
public sealed class DashboardView : UserControl
{
    // store supplies events; open navigates to Assets. This view owns and replaces the display table.
    readonly HistoryStore store;
    // open receives exact type/brand/model values for the selected group.
    readonly Action<string,string,string> open;
    // search matches canonical identity/administrative fields; unchecked from/to date controls do not restrict results.
    readonly TextBox search=new(){Width=220};
    // Optional local date boundaries; Checked=false leaves the corresponding side unbounded.
    readonly DateTimePicker from=new(){ShowCheckBox=true,Checked=false,Format=DateTimePickerFormat.Short,Width=130},to=new(){ShowCheckBox=true,Checked=false,Format=DateTimePickerFormat.Short,Width=130};
    // refresh explicitly rebuilds counts; caption labels and summary/help are presentation-only.
    readonly CheckBox includeRetired=new(){AutoSize=true};
    readonly Button refresh=new(){AutoSize=true};
    // Translated search/from/to captions, separate from user input.
    readonly Label searchLabel=new(){AutoSize=true}, fromLabel=new(){AutoSize=true},toLabel=new(){AutoSize=true};
    // summary displays computed counts; help describes drill-down behavior.
    readonly Label summary=new(){Dock=DockStyle.Fill}, help=new(){AutoSize=true};
    // grid displays one row per grouping, with an integer quantity for correct numeric sorting.
    readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,RowHeadersVisible=false};
    // Owned grouping data, replaced and disposed whenever the view is rebuilt.
    DataTable? table;
    /// <summary>Builds search/dates/summary/group table and a double-click callback to Assets.</summary>
    /// <param name="history">History store; tests supply an isolated instance.</param>
    /// <param name="onOpen">Callback opening Assets for a type/brand/model grouping.</param>
    public DashboardView(HistoryStore history,Action<string,string,string> onOpen)
    {
        store=history;open=onOpen;BackColor=Color.White;Dock=DockStyle.Fill;Padding=new Padding(16);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
        layout.RowStyles.Add(new(SizeType.Absolute,70));layout.RowStyles.Add(new(SizeType.Absolute,80));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,50));
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill};filters.Controls.AddRange([searchLabel,search,fromLabel,from,toLabel,to,refresh,includeRetired]);layout.Controls.Add(filters);layout.Controls.Add(summary);layout.Controls.Add(grid);layout.Controls.Add(help);Controls.Add(layout);
        includeRetired.CheckedChanged+=(_,_)=>Run();
        refresh.Click+=(_,_)=>Run();search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Run();}};
        grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0){var r=grid.Rows[e.RowIndex];open(Convert.ToString(r.Cells[0].Value)!,Convert.ToString(r.Cells[1].Value)!,Convert.ToString(r.Cells[2].Value)!);}};
        from.Checked=to.Checked=false;RefreshLanguage();
    }
    /// <summary>Refreshes and displays readable filter/query failures.</summary>
    void Run(){try{RefreshData();}catch(Exception ex){if(Environment.GetCommandLineArgs().Contains("--self-test"))throw;MessageBox.Show(FindForm(),ex.Message,"TagPrinter");}}
    /// <summary>Translates captions and rebuilds summary/columns.</summary>
    public void RefreshLanguage(){searchLabel.Text=Ui.T("Buscar","Search");fromLabel.Text=Ui.T("Actualizados desde","Updated from");toLabel.Text=Ui.T("Hasta","To");refresh.Text=Ui.T("Buscar / actualizar","Search / refresh");search.PlaceholderText=Ui.T("Buscar activo…","Search assets…");from.AccessibleName=Ui.T("Desde","From");to.AccessibleName=Ui.T("Hasta","To");includeRetired.Text=Ui.T("Incluir dados de baja","Include retired");help.Text=Ui.T("Doble clic para abrir Activos. Las fechas filtran la última modificación del activo.","Double-click to open Assets. Dates filter each asset's last update.");RefreshData();}
    /// <summary>Excludes empty serials and selects greatest event ID per trimmed, case-insensitive serial.</summary>
    /// <param name="events">Candidate events already filtered to the desired scope.</param>
    /// <returns>One latest matching event per nonblank normalized serial.</returns>
    internal static List<HistoryEvent> UniqueAssets(IEnumerable<HistoryEvent> events)=>events.Where(e=>!string.IsNullOrWhiteSpace(e.State.Serial)).OrderByDescending(e=>e.Id).GroupBy(e=>e.State.Serial.Trim(),StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
    /// <summary>
    /// Filters current master records by last-update dates, groups in SQL and separately totals global
    /// label log activity without confusing it with canonical asset counts.
    /// </summary>
    public void RefreshData()
    {
        if(from.Checked && to.Checked && from.Value.Date>to.Value.Date)throw new ArgumentException(Ui.T("Revisa las fechas.","Check the date range."));
        DateTimeOffset? start=from.Checked?new DateTimeOffset(from.Value.Date).ToUniversalTime():null,end=to.Checked?new DateTimeOffset(to.Value.Date.AddDays(1)).ToUniversalTime():null;
        var data=new AssetStore(store).Dashboard(search.Text.Trim(),start,end,includeRetired.Checked);
        var next=new DataTable();next.Columns.Add(Ui.T("Tipo de activo","Asset type"));next.Columns.Add(Ui.T("Marca","Brand"));next.Columns.Add(Ui.T("Modelo","Model"));next.Columns.Add(Ui.T("Cantidad","Quantity"),typeof(long));
        foreach(var group in data.Groups)next.Rows.Add(group.Type,group.Brand,group.Model,group.Count);
        var previous=table;table=next;grid.DataSource=next;previous?.Dispose();var labels=store.LabelTotals();
        long State(string key)=>data.States.GetValueOrDefault(key);
        summary.Text=string.Format(Ui.T("Activos: {0} · Disponibles: {1} · Asignados: {2} · En reparación: {3} · Dados de baja: {4} · Pendientes revisión: {5}\nHistorial global de etiquetas: {6} exportaciones · {7} trabajos enviados · {8} copias solicitadas.","Assets: {0} · Available: {1} · Assigned: {2} · In repair: {3} · Retired: {4} · Needs review: {5}\nGlobal label log: {6} exports · {7} submitted jobs · {8} requested copies."),data.Groups.Sum(g=>g.Count),State("Available"),State("Assigned"),State("Repair"),State("Retired"),State("Unspecified"),labels.Exports,labels.Jobs,labels.Copies);

    }
    /// <summary>Disposes the owned display table before child controls.</summary>
    /// <param name="disposing">True for explicit managed disposal; passed to the base class.</param>
    protected override void Dispose(bool disposing){if(disposing)table?.Dispose();base.Dispose(disposing);}
}
