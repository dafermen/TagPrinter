using System.Data;
using System.Globalization;
namespace TagPrinter;

/// <summary>Paged, read-only field differences for service revisions. Closing interrupts any active read.</summary>
internal sealed class ServiceChangesView : UserControl
{
    private readonly ServiceStore store;
    private readonly long id;
    private readonly BackgroundRead<DatabasePage<ServiceChange>> reads=new();
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,BackgroundColor=Color.White,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
    private readonly Button previous=new(){Text="◀",Width=42},next=new(){Text="▶",Width=42};
    private readonly Label summary=new(){AutoSize=true,Padding=new Padding(5,6,5,0)};
    private DataTable? table;
    private int page;
    /// <summary>Owns only one page of revisions; history cannot be edited or deleted from this view.</summary>
    internal ServiceChangesView(ServiceStore store,long id)
    {
        this.store=store;this.id=id;Dock=DockStyle.Fill;var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,Padding=new Padding(8)};footer.Controls.AddRange([previous,summary,next]);Controls.Add(grid);Controls.Add(footer);previous.Click+=(_,_)=>{if(page>0){page--;RefreshData();}};next.Click+=(_,_)=>{page++;RefreshData();};Load+=(_,_)=>RefreshData();
    }
    /// <summary>Captures page number before starting the background read.</summary>
    private void RefreshData(){int requested=page;reads.Submit(token=>store.ReadChanges(id,requested,50,token),Bind,error=>summary.Text=error.Message,busy=>{grid.Enabled=!busy;if(busy){previous.Enabled=next.Enabled=false;summary.Text=Ui.T("Cargando…","Loading…");}});}
    /// <summary>Expands only this page's changed fields; notes/cost/currency remain separate, readable cells.</summary>
    private void Bind(DatabasePage<ServiceChange> result)
    {
        page=result.Page;var data=new DataTable();foreach(string key in new[]{Ui.T("Fecha local","Local date"),Ui.T("Revisión","Revision"),Ui.T("Campo","Field"),Ui.T("Antes","Before"),Ui.T("Después","After")})data.Columns.Add(key);
        foreach(var change in result.Rows)
        {
            var before=Values(change.Before);var after=Values(change.After);foreach(var pair in after)if(!before.TryGetValue(pair.Key,out var old) || old!=pair.Value)data.Rows.Add(DateTimeOffset.Parse(change.AtUtc,CultureInfo.InvariantCulture).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),change.After.Revision,pair.Key,old??"",pair.Value);
        }
        var previousTable=table;table=data;grid.DataSource=data;previousTable?.Dispose();summary.Text=$"{page+1} / {Math.Max(1,(result.Total+49)/50)} · {result.Total} "+Ui.T("revisiones","revisions");previous.Enabled=page>0;next.Enabled=(page+1)*50<result.Total;
    }
    /// <summary>Maps business fields explicitly, keeping dates/amounts invariant and localized keys out of stored snapshots.</summary>
    private static Dictionary<string,string> Values(ServiceRecord? s)=>s is null?[]:new()
    {
        [Ui.T("Descripción","Description")]=s.Title,[Ui.T("Estado","Status")]=ServiceStore.Caption(s.Status),[Ui.T("Tipo","Type")]=ServiceStore.Caption(s.Category),[Ui.T("Inicio","Start")]=s.StartDate,[Ui.T("Programado / vence","Scheduled / expires")]=s.DueDate,[Ui.T("Finalización","Completion")]=s.CompletedDate,[Ui.T("Proveedor","Provider")]=s.Provider,[Ui.T("Referencia","Reference")]=s.Reference,[Ui.T("Costo","Cost")]=s.Cost?.ToString("0.00",CultureInfo.InvariantCulture)??"",[Ui.T("Moneda","Currency")]=s.Currency,[Ui.T("Notas","Notes")]=s.Notes
    };
    /// <summary>Releases owned reads/table before the controls are disposed.</summary>
    /// <param name="disposing">True for explicit managed disposal.</param>
    protected override void Dispose(bool disposing){if(disposing){reads.Dispose();table?.Dispose();}base.Dispose(disposing);}
}
