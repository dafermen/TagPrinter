using System.Data;
using System.Globalization;
namespace TagPrinter;

/// <summary>Read-only paged attempts, duplicate feedback, withdrawals and lifecycle evidence, with full-scope CSV export.</summary>
internal sealed class CountActionsView : UserControl
{
    private readonly CountStore store;
    private readonly long sessionId;
    private readonly BackgroundRead<DatabasePage<CountAction>> reads=new();
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,BackgroundColor=Color.White,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
    private readonly Button previous=new(){Text="◀",Width=42},next=new(){Text="▶",Width=42};
    private readonly Label summary=new(){AutoSize=true,Padding=new Padding(4,7,4,0)};
    private DataTable? table;
    private int page;
    /// <summary>Reads only 50 actions per page. Export includes every action in revision order.</summary>
    internal CountActionsView(CountStore store,long sessionId)
    {
        this.store=store;this.sessionId=sessionId;Dock=DockStyle.Fill;var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,Padding=new Padding(8)};var export=new Button{AutoSize=true,Text=Ui.T("Exportar lecturas CSV…","Export readings CSV…")};footer.Controls.AddRange([previous,summary,next,export]);Controls.Add(grid);Controls.Add(footer);Load+=(_,_)=>RefreshData();previous.Click+=(_,_)=>{if(page>0){page--;RefreshData();}};next.Click+=(_,_)=>{page++;RefreshData();};
        export.Click+=(_,_)=>{using var file=new SaveFileDialog{Filter="CSV|*.csv",DefaultExt="csv",AddExtension=true,FileName="TagPrinter-count-readings.csv"};if(file.ShowDialog(FindForm())!=DialogResult.OK)return;string path=file.FileName;try{ExportProgressDialog.Run(FindForm(),(progress,token)=>store.ExportActionsCsv(path,sessionId,token,progress));}catch(Exception error){if(Environment.GetCommandLineArgs().Contains("--self-test"))throw;MessageBox.Show(FindForm(),error.Message,"TagPrinter");}};FluentIcons.ApplyTree(this);
    }
    /// <summary>Captures page before worker execution; only its latest result can reach controls.</summary>
    private void RefreshData(){int requested=page;reads.Submit(token=>store.Actions(sessionId,requested,50,token),result=>
    {
        page=result.Page;var data=new DataTable();foreach(string label in new[]{Ui.T("Revisión","Revision"),Ui.T("Fecha local","Local date"),Ui.T("Acción","Action"),Ui.T("Modo","Mode"),Ui.T("Lectura original","Original input"),Ui.T("Resultado","Result")})data.Columns.Add(label);foreach(var a in result.Rows)data.Rows.Add(a.Revision,DateTimeOffset.Parse(a.AtUtc,CultureInfo.InvariantCulture).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),CountStore.Caption(a.Action),CountStore.Caption(a.InputMode),a.InputValue,CountStore.Caption(a.Result));var old=table;table=data;grid.DataSource=data;old?.Dispose();summary.Text=$"{page+1} / {Math.Max(1,(result.Total+49)/50)} · {result.Total}";previous.Enabled=page>0;next.Enabled=(page+1)*50<result.Total;
    },error=>{grid.DataSource=null;summary.Text=error.Message;},busy=>{grid.Enabled=!busy;if(busy)previous.Enabled=next.Enabled=false;});}
    /// <summary>Interrupts readers before disposing the owned display projection.</summary>
    /// <param name="disposing">True for explicit managed disposal.</param>
    protected override void Dispose(bool disposing){if(disposing){reads.Dispose();table?.Dispose();}base.Dispose(disposing);}
}
