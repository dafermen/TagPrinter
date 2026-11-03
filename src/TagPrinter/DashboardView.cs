using System.Data;
namespace TagPrinter;
/// <summary>
/// Groups distinct serials by type, brand and model and summarizes activity. It is not verified physical
/// inventory.
/// </summary>
public sealed class DashboardView : UserControl
{
    // store supplies events; open navigates to Reports. This view owns and replaces the display table.
    readonly HistoryStore store;
    // open receives exact type/brand/model values for the selected group.
    readonly Action<string,string,string> open;
    // search matches six asset fields; unchecked from/to date controls do not restrict results.
    readonly TextBox search=new(){Width=220};
    // Optional local date boundaries; Checked=false leaves the corresponding side unbounded.
    readonly DateTimePicker from=new(){ShowCheckBox=true,Checked=false,Format=DateTimePickerFormat.Short,Width=130},to=new(){ShowCheckBox=true,Checked=false,Format=DateTimePickerFormat.Short,Width=130};
    // refresh explicitly rebuilds counts; caption labels and summary/help are presentation-only.
    readonly Button refresh=new(){AutoSize=true};
    // Translated search/from/to captions, separate from user input.
    readonly Label searchLabel=new(){AutoSize=true}, fromLabel=new(){AutoSize=true},toLabel=new(){AutoSize=true};
    // summary displays computed counts; help describes drill-down behavior.
    readonly Label summary=new(){Dock=DockStyle.Fill}, help=new(){AutoSize=true};
    // grid displays one row per grouping, with an integer quantity for correct numeric sorting.
    readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,RowHeadersVisible=false};
    // Owned grouping data, replaced and disposed whenever the view is rebuilt.
    DataTable? table;
    /// <summary>Builds search/dates/summary/group table and a double-click callback to Reports.</summary>
    /// <param name="history">History store; tests supply an isolated instance.</param>
    /// <param name="onOpen">Callback opening Reports for a type/brand/model grouping.</param>
    public DashboardView(HistoryStore history,Action<string,string,string> onOpen)
    {
        store=history;open=onOpen;BackColor=Color.White;Dock=DockStyle.Fill;Padding=new Padding(16);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
        layout.RowStyles.Add(new(SizeType.Absolute,70));layout.RowStyles.Add(new(SizeType.Absolute,80));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,50));
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill};filters.Controls.AddRange([searchLabel,search,fromLabel,from,toLabel,to,refresh]);layout.Controls.Add(filters);layout.Controls.Add(summary);layout.Controls.Add(grid);layout.Controls.Add(help);Controls.Add(layout);
        refresh.Click+=(_,_)=>Run();search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Run();}};
        grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0){var r=grid.Rows[e.RowIndex];open(Convert.ToString(r.Cells[0].Value)!,Convert.ToString(r.Cells[1].Value)!,Convert.ToString(r.Cells[2].Value)!);}};
        from.Checked=to.Checked=false;RefreshLanguage();
    }
    /// <summary>Refreshes and displays readable filter/query failures.</summary>
    void Run(){try{RefreshData();}catch(Exception ex){MessageBox.Show(FindForm(),ex.Message,"TagPrinter");}}
    /// <summary>Translates captions and rebuilds summary/columns.</summary>
    public void RefreshLanguage(){searchLabel.Text=Ui.T("Buscar","Search");fromLabel.Text=Ui.T("Desde","From");toLabel.Text=Ui.T("Hasta","To");refresh.Text=Ui.T("Buscar / actualizar","Search / refresh");search.PlaceholderText=Ui.T("Buscar activo…","Search assets…");from.AccessibleName=Ui.T("Desde","From");to.AccessibleName=Ui.T("Hasta","To");help.Text=Ui.T("Doble clic en un grupo para abrir Reportes. Fechas opcionales: desde / hasta.","Double-click a group to open Reports. Optional dates: from / to.");RefreshData();}
    /// <summary>Excludes empty serials and selects greatest event ID per trimmed, case-insensitive serial.</summary>
    /// <param name="events">Candidate events already filtered to the desired scope.</param>
    /// <returns>One latest matching event per nonblank normalized serial.</returns>
    internal static List<HistoryEvent> UniqueAssets(IEnumerable<HistoryEvent> events)=>events.Where(e=>!string.IsNullOrWhiteSpace(e.State.Serial)).OrderByDescending(e=>e.Id).GroupBy(e=>e.State.Serial.Trim(),StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
    /// <summary>
    /// Filters active events first, deduplicates serials, groups snapshots and separately totals
    /// events/jobs/copies.
    /// </summary>
    public void RefreshData()
    {
        if(from.Checked && to.Checked && from.Value.Date>to.Value.Date)throw new ArgumentException(Ui.T("Revisa las fechas.","Check the date range."));
        DateTimeOffset? start=from.Checked?new DateTimeOffset(from.Value.Date).ToUniversalTime():null,end=to.Checked?new DateTimeOffset(to.Value.Date.AddDays(1)).ToUniversalTime():null;
        // Filter first, then deduplicate: each serial uses its latest matching event, not necessarily its
        // latest event across the whole database.
        var events=store.Query(new HistoryQuery(search.Text.Trim(),FromUtc:start,ToUtcExclusive:end));var assets=UniqueAssets(events);
        var next=new DataTable();next.Columns.Add(Ui.T("Tipo de activo","Asset type"));next.Columns.Add(Ui.T("Marca","Brand"));next.Columns.Add(Ui.T("Modelo","Model"));next.Columns.Add(Ui.T("Cantidad","Quantity"),typeof(int));
        // Each representative serial adds one to its group. Blank serials contribute to action totals, never
        // to unique-asset counts.
        foreach(var group in assets.GroupBy(e=>(e.State.AssetType.Trim(),e.State.Brand.Trim(),e.State.Model.Trim())).OrderByDescending(g=>g.Count()))next.Rows.Add(group.Key.Item1,group.Key.Item2,group.Key.Item3,group.Count());
        var previous=table;table=next;grid.DataSource=next;previous?.Dispose();
        // Actions, jobs and requested copies are different measures. Submission/copy totals do not confirm
        // physical printing.
        summary.Text=string.Format(Ui.T("Activos únicos por serial: {0} · Acciones: {1} · Sin serial: {2}\nEtiquetas exportadas: {3} · Trabajos enviados: {4} · Copias enviadas: {5}","Unique assets by serial: {0} · Actions: {1} · Without serial: {2}\nTags exported: {3} · Jobs submitted: {4} · Copies submitted: {5}"),assets.Count,events.Count,events.Count(e=>string.IsNullOrWhiteSpace(e.State.Serial)),events.Count(e=>e.Action=="ExportTag"),events.Count(e=>e.Action=="PrintSubmitted"),events.Where(e=>e.Action=="PrintSubmitted").Sum(e=>(long)e.State.Copies));
    }
    /// <summary>Disposes the owned display table before child controls.</summary>
    /// <param name="disposing">True for explicit managed disposal; passed to the base class.</param>
    protected override void Dispose(bool disposing){if(disposing)table?.Dispose();base.Dispose(disposing);}
}
