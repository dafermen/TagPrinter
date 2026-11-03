using System.Data;
namespace TagPrinter;
public sealed class DashboardView : UserControl
{
    readonly HistoryStore store;
    readonly Action<string,string,string> open;
    readonly TextBox search=new(){Width=220};
    readonly DateTimePicker from=new(){ShowCheckBox=true,Checked=false,Format=DateTimePickerFormat.Short,Width=130},to=new(){ShowCheckBox=true,Checked=false,Format=DateTimePickerFormat.Short,Width=130};
    readonly Button refresh=new(){AutoSize=true};
    readonly Label searchLabel=new(){AutoSize=true}, fromLabel=new(){AutoSize=true},toLabel=new(){AutoSize=true};
    readonly Label summary=new(){Dock=DockStyle.Fill}, help=new(){AutoSize=true};
    readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,RowHeadersVisible=false};
    DataTable? table;
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
    void Run(){try{RefreshData();}catch(Exception ex){MessageBox.Show(FindForm(),ex.Message,"TagPrinter");}}
    public void RefreshLanguage(){searchLabel.Text=Ui.T("Buscar","Search");fromLabel.Text=Ui.T("Desde","From");toLabel.Text=Ui.T("Hasta","To");refresh.Text=Ui.T("Buscar / actualizar","Search / refresh");search.PlaceholderText=Ui.T("Buscar activo…","Search assets…");from.AccessibleName=Ui.T("Desde","From");to.AccessibleName=Ui.T("Hasta","To");help.Text=Ui.T("Doble clic en un grupo para abrir Reportes. Fechas opcionales: desde / hasta.","Double-click a group to open Reports. Optional dates: from / to.");RefreshData();}
    internal static List<HistoryEvent> UniqueAssets(IEnumerable<HistoryEvent> events)=>events.Where(e=>!string.IsNullOrWhiteSpace(e.State.Serial)).OrderByDescending(e=>e.Id).GroupBy(e=>e.State.Serial.Trim(),StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
    public void RefreshData()
    {
        if(from.Checked && to.Checked && from.Value.Date>to.Value.Date)throw new ArgumentException(Ui.T("Revisa las fechas.","Check the date range."));
        DateTimeOffset? start=from.Checked?new DateTimeOffset(from.Value.Date).ToUniversalTime():null,end=to.Checked?new DateTimeOffset(to.Value.Date.AddDays(1)).ToUniversalTime():null;
        var events=store.Query(new HistoryQuery(search.Text.Trim(),FromUtc:start,ToUtcExclusive:end));var assets=UniqueAssets(events);
        var next=new DataTable();next.Columns.Add(Ui.T("Tipo de activo","Asset type"));next.Columns.Add(Ui.T("Marca","Brand"));next.Columns.Add(Ui.T("Modelo","Model"));next.Columns.Add(Ui.T("Cantidad","Quantity"),typeof(int));
        foreach(var group in assets.GroupBy(e=>(e.State.AssetType.Trim(),e.State.Brand.Trim(),e.State.Model.Trim())).OrderByDescending(g=>g.Count()))next.Rows.Add(group.Key.Item1,group.Key.Item2,group.Key.Item3,group.Count());
        var previous=table;table=next;grid.DataSource=next;previous?.Dispose();
        summary.Text=string.Format(Ui.T("Activos únicos por serial: {0} · Acciones: {1} · Sin serial: {2}\nEtiquetas exportadas: {3} · Trabajos enviados: {4} · Copias enviadas: {5}","Unique assets by serial: {0} · Actions: {1} · Without serial: {2}\nTags exported: {3} · Jobs submitted: {4} · Copies submitted: {5}"),assets.Count,events.Count,events.Count(e=>string.IsNullOrWhiteSpace(e.State.Serial)),events.Count(e=>e.Action=="ExportTag"),events.Count(e=>e.Action=="PrintSubmitted"),events.Where(e=>e.Action=="PrintSubmitted").Sum(e=>(long)e.State.Copies));
    }
    protected override void Dispose(bool disposing){if(disposing)table?.Dispose();base.Dispose(disposing);}
}
