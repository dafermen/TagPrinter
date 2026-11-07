using System.Data;
using System.Globalization;
namespace TagPrinter;

/// <summary>Bounded administrative-audit pages; only the selected page's before/after snapshots are deserialized.</summary>
internal sealed class MovementHistoryView : UserControl
{
    private readonly AssetStore store;
    private readonly string uid;
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White};
    private readonly Button previous=new(){Text="◀",Width=42},next=new(){Text="▶",Width=42};
    private readonly Label summary=new(){AutoSize=true,Padding=new Padding(4,6,0,0)};
    private readonly BackgroundRead<DatabasePage<AssetMovement>> reads=new();
    private int page;
    private DataTable? table;
    /// <summary>Builds a paged view for one stable identity; closing interrupts current reading.</summary>
    internal MovementHistoryView(AssetStore store,string uid)
    {
        this.store=store;this.uid=uid;Dock=DockStyle.Fill;var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,Padding=new Padding(8)};footer.Controls.AddRange([previous,summary,next]);Controls.Add(grid);Controls.Add(footer);
        previous.Click+=(_,_)=>{if(page>0){page--;RefreshData();}};next.Click+=(_,_)=>{page++;RefreshData();};Load+=(_,_)=>RefreshData();
    }
    /// <summary>Reads one consistent page with latest-only UI delivery.</summary>
    private void RefreshData()
    {
        int requested=page;reads.Submit(token=>store.ReadMovements(uid,requested,50,token),Bind,error=>summary.Text=error.Message,busy=>{grid.Enabled=!busy;if(busy){previous.Enabled=next.Enabled=false;summary.Text=Ui.T("Cargando movimientos…","Loading movements…");}});
    }
    /// <summary>Expands field differences only for these 50 movements; historical metadata remains immutable.</summary>
    private void Bind(DatabasePage<AssetMovement> result)
    {
        page=result.Page;var data=new DataTable();foreach(string label in new[]{Ui.T("Fecha","Date"),Ui.T("Acción","Action"),Ui.T("Campo","Field"),Ui.T("Antes","Before"),Ui.T("Después","After"),Ui.T("Usuario","User")})data.Columns.Add(label);
        foreach(var movement in result.Rows)
        {
            string action=movement.Action switch{"Migrated"=>Ui.T("Desde historial","From history"),"Registered"=>Ui.T("Registrado al etiquetar","Registered from label"),"Created"=>Ui.T("Creado","Created"),"Imported"=>Ui.T("Importado","Imported"),"ImportUpdated"=>Ui.T("Actualizado por CSV","Updated from CSV"),"Assigned"=>Ui.T("Asignado","Assigned"),"Returned"=>Ui.T("Devuelto","Returned"),"RepairStarted"=>Ui.T("Enviado a reparación","Sent to repair"),_=>Ui.T("Modificado","Updated")};
            void Row(string field,string before,string after)=>data.Rows.Add(DateTimeOffset.Parse(movement.AtUtc,CultureInfo.InvariantCulture).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),action,field,before,after,movement.Actor);
            foreach(var change in AssetCsv.Changes(new(0,movement.Before,movement.After)))Row(AssetCsv.Caption(change.Field),change.Field=="Status"&&change.Before.Length>0?AssetStore.StatusCaption(change.Before):change.Before,change.Field=="Status"?AssetStore.StatusCaption(change.After):change.After);
            if(movement.Note.Length>0)Row(Ui.T("Motivo","Reason"),"",movement.Note);
        }
        var old=table;table=data;grid.DataSource=data;old?.Dispose();summary.Text=$"{page+1} / {Math.Max(1,(result.Total+49)/50)} · {result.Total} "+Ui.T("movimientos","movements");previous.Enabled=page>0;next.Enabled=(page+1)*50<result.Total;
    }
    /// <summary>Releases the reader and owned projection.</summary>
    /// <param name="disposing">True for explicit managed disposal.</param>
    protected override void Dispose(bool disposing){if(disposing){reads.Dispose();table?.Dispose();}base.Dispose(disposing);}
}
