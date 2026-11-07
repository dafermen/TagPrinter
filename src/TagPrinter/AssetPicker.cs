using System.Data;
namespace TagPrinter;

/// <summary>Read-only, paged asset picker. Selecting an asset neither edits its record nor prints a label.</summary>
internal sealed class AssetPicker : AppWindow
{
    private readonly AssetStore store;
    private readonly BackgroundRead<DatabasePage<AssetRecord>> reads=new();
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,MultiSelect=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White};
    private readonly TextBox search=new(){Width=330};
    private readonly Button previous=new(){Text="◀",Width=42},next=new(){Text="▶",Width=42},choose=new(){AutoSize=true};
    private readonly Label summary=new(){AutoSize=true,Padding=new Padding(5,6,5,0)};
    private DataTable? table;
    private int page;
    internal AssetRecord? Selected {get;private set;}
    /// <summary>Offers at most 50 current assets per query, including explicitly retired assets for historical record entry.</summary>
    internal AssetPicker(HistoryStore history)
    {
        store=new(history);Text=Ui.T("Seleccionar activo","Choose an asset");Width=950;Height=550;MinimumSize=new Size(650,440);StartPosition=FormStartPosition.CenterParent;
        var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=52,Padding=new Padding(12)};var refresh=new Button{Text=Ui.T("Buscar","Search"),AutoSize=true};search.PlaceholderText=Ui.T("Código, serial, tipo, marca o modelo","Code, serial, type, brand or model");top.Controls.AddRange([search,refresh]);var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,Padding=new Padding(12,4,0,0)};choose.Text=Ui.T("Seleccionar activo","Choose asset");var cancel=new Button{AutoSize=true,Text=Ui.T("Cancelar","Cancel"),DialogResult=DialogResult.Cancel};bottom.Controls.AddRange([previous,summary,next,choose,cancel]);Controls.Add(grid);Controls.Add(top);Controls.Add(bottom);CancelButton=cancel;
        refresh.Click+=(_,_)=>{page=0;RefreshData();};search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;page=0;RefreshData();}};previous.Click+=(_,_)=>{if(page>0){page--;RefreshData();}};next.Click+=(_,_)=>{page++;RefreshData();};choose.Click+=(_,_)=>SelectAsset();grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)SelectAsset();};Shown+=(_,_)=>RefreshData();FluentIcons.ApplyTree(this);
    }
    /// <summary>Captures search/page on the UI thread; only SQL runs on the background worker.</summary>
    private void RefreshData()
    {
        string text=search.Text;int requested=page;reads.Submit(token=>store.ReadPage(new(Search:text),requested,50,"Updated",true,token),result=>
        {
            page=result.Page;var data=new DataTable();foreach(string key in new[]{"Uid","Code","AssetType","Brand","Model","Serial","Status"})data.Columns.Add(key);foreach(var asset in result.Rows)data.Rows.Add(asset.Uid,asset.Code,asset.AssetType,asset.Brand,asset.Model,asset.Serial,AssetStore.StatusCaption(asset.Status));var old=table;table=data;grid.DataSource=data;old?.Dispose();grid.Columns["Uid"].Visible=false;string[] captions=["Uid",Ui.T("Código","Code"),Ui.T("Tipo","Type"),Ui.T("Marca","Brand"),Ui.T("Modelo","Model"),"Serial",Ui.T("Estado","Status")];for(int i=0;i<captions.Length;i++)grid.Columns[i].HeaderText=captions[i];summary.Text=$"{page+1} / {Math.Max(1,(result.Total+49)/50)} · {result.Total}";previous.Enabled=page>0;next.Enabled=(page+1)*50<result.Total;
        },error=>summary.Text=error.Message,busy=>{grid.Enabled=choose.Enabled=!busy;if(busy){previous.Enabled=next.Enabled=false;summary.Text=Ui.T("Cargando…","Loading…");}});
    }
    /// <summary>Re-resolves the selected identity so an old grid row cannot return stale asset metadata.</summary>
    private void SelectAsset(){if(reads.IsBusy || grid.CurrentRow is null)return;Selected=store.Get(grid.CurrentRow.Cells["Uid"].Value.ToString()!);if(Selected is not null)DialogResult=DialogResult.OK;}
    /// <summary>Interrupts reads and releases the owned projection.</summary>
    /// <param name="disposing">True for explicit managed disposal.</param>
    protected override void Dispose(bool disposing){if(disposing){reads.Dispose();table?.Dispose();}base.Dispose(disposing);}
}
