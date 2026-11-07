using System.Data;
using System.Globalization;
namespace TagPrinter;

/// <summary>Paged local administration with reusable vocabulary, explicit workflows and reviewed master-data CSV.</summary>
internal sealed partial class AssetsView : UserControl
{
    private readonly HistoryStore history;
    private readonly AssetStore store;
    private readonly Action<AssetRecord> loadLabel;
    private readonly TextBox search=new(){Width=230};
    private readonly ComboBox status=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=160,Tag="UserData"};
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=true,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,RowHeadersVisible=false,BackgroundColor=Color.White};
    private readonly Label summary=new(){Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0)};
    private readonly List<(Button Button,string Es,string En)> captions=[];
    private readonly Label pageLabel=new(){AutoSize=true,Padding=new Padding(4,7,4,0)};
    private readonly Button previous=new(){Text="◀",Width=42},next=new(){Text="▶",Width=42};
    private int page;
    private string sortKey="Updated";
    private bool sortDescending=true,translating;
    // Dashboard scope carries exact raw values and dates. Ordinary controls may further narrow this scope.
    private AssetFilter scope=new();
    private DataTable? table;
    private Action? resizeToolbar;
    private sealed record StatusChoice(string Key,string Caption){public override string ToString()=>Caption;}
    /// <summary>Builds wrapped toolbars above the grid; commands never print automatically.</summary>
    internal AssetsView(HistoryStore history,Action<AssetRecord> onLoad)
    {
        this.history=history;store=new(history);loadLabel=onLoad;Dock=DockStyle.Fill;Padding=new Padding(16);BackColor=Color.White;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};root.ColumnStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,160));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,80));
        var toolbar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};toolbar.ColumnStyles.Add(new(SizeType.Percent,100));
        var bars=new List<FlowLayoutPanel>();
        FlowLayoutPanel Row(){var row=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty};toolbar.RowStyles.Add(new(SizeType.Absolute,40));toolbar.Controls.Add(row,0,bars.Count);bars.Add(row);return row;}
        var filters=Row();filters.Controls.AddRange([search,status]);
        Button Add(FlowLayoutPanel row,string es,string en,Action action){var button=new Button{AutoSize=true};captions.Add((button,es,en));button.Click+=(_,_)=>Run(action);row.Controls.Add(button);return button;}
        Add(filters,"Buscar / actualizar","Search / refresh",()=>{page=0;RefreshData();});Add(filters,"Limpiar filtros","Clear filters",()=>ShowFilter(new()));
        var actions=Row();Add(actions,"Nuevo activo","New asset",()=>Edit(new()));Add(actions,"Editar","Edit",()=>Edit(Selected()));
        Add(actions,"Duplicar","Duplicate",()=>Edit(Selected() with {Uid="",Number=0,Revision=0,Serial="",PartNumber="",MacAddress="",Status="Available",Custodian="",Location="",Notes="",CreatedUtc="",UpdatedUtc=""}));
        Add(actions,"Asignar","Assign",()=>ShowMovementAction("Assigned"));Add(actions,"Devolver","Return",()=>ShowMovementAction("Returned"));Add(actions,"Enviar a reparación","Send to repair",()=>ShowMovementAction("RepairStarted"));
        Add(actions,"Dar de baja","Retire",()=>{var item=Selected();if(MessageBox.Show(FindForm(),Ui.T("¿Dar de baja este activo? Su ficha e historial se conservarán.","Retire this asset? Its record and history will be preserved."),"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){store.Save(item with {Status="Retired"});RefreshData();}});
        Add(actions,"Cargar etiqueta","Load label",()=>loadLabel(Selected()));Add(actions,"Movimientos","Movements",ShowMovements);Add(actions,"Catálogos…","Catalogs…",ShowCatalogs);
        var files=Row();Add(files,"Exportar activos CSV…","Export assets CSV…",ExportAssets);Add(files,"Importar activos CSV…","Import assets CSV…",ImportAssets);Add(files,"Plantilla de activos…","Asset CSV template…",SaveAssetTemplate);
        previous.Click+=(_,_)=>Run(()=>{if(page>0){page--;RefreshData();}});next.Click+=(_,_)=>Run(()=>{page++;RefreshData();});files.Controls.AddRange([previous,pageLabel,next]);
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;page=0;Run(RefreshData);}};status.SelectedIndexChanged+=(_,_)=>{if(!translating){page=0;Run(RefreshData);}};
        grid.ColumnHeaderMouseClick+=(_,e)=>{if(e.ColumnIndex<0)return;string key=grid.Columns[e.ColumnIndex].Name;sortDescending=key==sortKey?!sortDescending:false;sortKey=key;page=0;Run(RefreshData);};
        grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)Run(()=>Edit(Selected()));};root.Controls.Add(toolbar,0,0);root.Controls.Add(grid,0,1);root.Controls.Add(summary,0,2);Controls.Add(root);
        bool resizing=false;
        resizeToolbar=()=>
        {
            if(resizing || toolbar.Width<100)return;resizing=true;
            try
            {
                toolbar.PerformLayout();int height=0;
                for(int i=0;i<bars.Count;i++)
                {
                    bars[i].PerformLayout();int rowHeight=Math.Max(36,bars[i].Controls.Cast<Control>().Max(c=>c.Bottom+c.Margin.Bottom)+4);toolbar.RowStyles[i].Height=rowHeight;height+=rowHeight;
                }
                root.RowStyles[0].Height=height+toolbar.Margin.Vertical;
            }
            finally{resizing=false;}
        };
        toolbar.SizeChanged+=(_,_)=>resizeToolbar();VisibleChanged+=(_,_)=>{if(Visible)resizeToolbar();};RefreshLanguage();
    }
    /// <summary>Returns the exact list/export criteria, combining Dashboard scope with current search/status controls.</summary>
    internal AssetFilter CurrentFilter=>scope with {Search=search.Text.Trim(),Status=(status.SelectedItem as StatusChoice)?.Key??""};
    /// <summary>Provides displayed row count without exposing mutable DataTable data to tests.</summary>
    internal int VisibleCount=>grid.Rows.Count;
    /// <summary>Checks real usable grid bounds, not merely the existence of bound rows.</summary>
    internal bool ListHasUsableSpace=>grid.Visible && grid.Width>100 && grid.Height>100;
    /// <summary>Checks that translated commands fit their measured wrapped rows.</summary>
    internal bool CommandsFit=>captions.All(item=>item.Button.Width>=item.Button.GetPreferredSize(Size.Empty).Width && item.Button.Parent!.ClientRectangle.Contains(item.Button.Bounds));
    /// <summary>Refreshes translated presentation while retaining stable state keys and navigation scope.</summary>
    internal void RefreshLanguage()
    {
        search.PlaceholderText=Ui.T("Buscar código, serial o responsable…","Search code, serial or custodian…");translating=true;string key=(status.SelectedItem as StatusChoice)?.Key??"";
        foreach(var item in captions)item.Button.Text=Ui.T(item.Es,item.En);
        status.Items.Clear();status.Items.Add(new StatusChoice("",Ui.T("Todos los estados","All statuses")));foreach(string state in AssetStore.Statuses)status.Items.Add(new StatusChoice(state,AssetStore.StatusCaption(state)));status.SelectedIndex=Math.Max(0,status.Items.Cast<StatusChoice>().ToList().FindIndex(s=>s.Key==key));translating=false;FluentIcons.ApplyTree(this);foreach(var item in captions)item.Button.Size=item.Button.GetPreferredSize(Size.Empty);resizeToolbar?.Invoke();RefreshData();
    }
    /// <summary>Reads only one SQL page using the same predicates as counts, Dashboard links and CSV export.</summary>
    internal void RefreshData()
    {
        var filter=CurrentFilter;long total=store.Count(filter);page=(int)Math.Min(page,Math.Max(0,(total-1)/50));var rows=store.Query(filter,page*50,50,sortKey,sortDescending);
        var data=new DataTable();foreach(string name in new[]{"Uid","Code","AssetType","Brand","Model","Serial","Status","Location","Custodian","PartNumber","MacAddress"})data.Columns.Add(name);data.Columns.Add("Updated",typeof(DateTime));
        foreach(var row in rows)data.Rows.Add(row.Uid,row.Code,row.AssetType,row.Brand,row.Model,row.Serial,AssetStore.StatusCaption(row.Status),row.Location,row.Custodian,row.PartNumber,row.MacAddress,DateTimeOffset.Parse(row.UpdatedUtc,CultureInfo.InvariantCulture).LocalDateTime);
        var old=table;table=data;grid.DataSource=data;old?.Dispose();grid.Columns["Uid"].Visible=false;
        string[] labels=["Uid",Ui.T("Código interno","Asset code"),Ui.T("Tipo de activo","Asset type"),Ui.T("Marca","Brand"),Ui.T("Modelo","Model"),"Serial",Ui.T("Estado","Status"),Ui.T("Ubicación","Location"),Ui.T("Responsable","Custodian"),Ui.T("No. de parte","Part number"),"MAC",Ui.T("Actualizado","Updated")];
        for(int i=0;i<labels.Length;i++){grid.Columns[i].HeaderText=labels[i];grid.Columns[i].Width=i==11||i==6?155:125;grid.Columns[i].SortMode=DataGridViewColumnSortMode.Programmatic;grid.Columns[i].HeaderCell.SortGlyphDirection=grid.Columns[i].Name==sortKey?(sortDescending?SortOrder.Descending:SortOrder.Ascending):SortOrder.None;}grid.Columns["Updated"].DefaultCellStyle.Format="yyyy-MM-dd HH:mm:ss";
        pageLabel.Text=$"{page+1} / {Math.Max(1,(total+49)/50)} · {total}";previous.Enabled=page>0;next.Enabled=(page+1)*50<total;
        summary.Text=$"{total} "+Ui.T("activos. Doble clic para editar. Cargar etiqueta no imprime.","assets. Double-click to edit. Load label does not print.")+Environment.NewLine+ScopeCaption(filter);
    }
    /// <summary>Describes hidden drill-down restrictions so the user can understand and clear the scope.</summary>
    private static string ScopeCaption(AssetFilter filter)
    {
        var parts=new List<string>();if(filter.Type is not null)parts.Add(filter.Type+" / "+filter.Brand+" / "+filter.Model);
        if(filter.Location is not null)parts.Add(Ui.T("Ubicación: ","Location: ")+(filter.Location.Length==0?Ui.T("Sin indicar","Not set"):filter.Location));
        if(filter.Custodian is not null)parts.Add(Ui.T("Responsable: ","Custodian: ")+(filter.Custodian.Length==0?Ui.T("Sin indicar","Not set"):filter.Custodian));
        if(filter.FromUtc is {} from)parts.Add(Ui.T("Desde ","From ")+from.LocalDateTime.ToString("yyyy-MM-dd"));if(filter.ToUtc is {} to)parts.Add(Ui.T("Antes de ","Before ")+to.LocalDateTime.ToString("yyyy-MM-dd"));
        if(filter.ExcludeRetired)parts.Add(Ui.T("Excluye dados de baja","Excludes retired"));return parts.Count==0?Ui.T("Una ficha por activo. Exportar activos CSV incluye todas las páginas filtradas.","One record per asset. Export assets CSV includes all filtered pages."):string.Join(" · ",parts)+Ui.T(" · Limpiar filtros para ver todos."," · Clear filters to see all.");
    }
    /// <summary>Preserves exact Dashboard search/date/status/group values in the destination.</summary>
    internal void ShowFilter(AssetFilter filter)
    {
        scope=filter;translating=true;search.Text=filter.Search;status.SelectedIndex=Math.Max(0,status.Items.Cast<StatusChoice>().ToList().FindIndex(s=>s.Key==filter.Status));translating=false;page=0;RefreshData();
    }
    /// <summary>Compatibility entry point for an unbounded description group.</summary>
    internal void ShowGroup(string type,string brand,string model)=>ShowFilter(new(Type:type,Brand:brand,Model:model));
    /// <summary>Loads fresh concurrency metadata by stable UID rather than trusting displayed text or row order.</summary>
    private AssetRecord Selected()=>grid.CurrentRow is null?throw new InvalidOperationException(Ui.T("Selecciona un activo.","Select an asset.")):store.Get(grid.CurrentRow.Cells["Uid"].Value.ToString()!)??throw new InvalidDataException("Asset not found.");
    /// <summary>Edits explicitly; failed validation leaves entered data in the open editor.</summary>
    private void Edit(AssetRecord source){using var dialog=CreateEditor(source);if(dialog.ShowDialog(FindForm())==DialogResult.OK)RefreshData();}
    /// <summary>Creates an editable dropdown of user vocabulary; new typed values are learned only on a successful save.</summary>
    private ComboBox CatalogInput(string kind,string value)
    {
        var combo=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDown,Tag="UserData",Name=kind,MaxLength=kind is "AssetType" or "Brand"?80:kind=="Model"?100:160,AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.ListItems};combo.Items.AddRange(new AssetCatalog(history).Values(kind).Cast<object>().ToArray());combo.Text=value;return combo;
    }
    /// <summary>Builds the real editor without showing it, enabling isolated tests of the actual save bindings.</summary>
    internal Form CreateEditor(AssetRecord source)
    {
        var dialog=new AppWindow{Text=source.Uid.Length==0?Ui.T("Nuevo activo","New asset"):source.Code,Width=640,Height=640,MinimumSize=new Size(540,600),StartPosition=FormStartPosition.CenterParent};
        var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,AutoScroll=true,Padding=new Padding(16)};fields.ColumnStyles.Add(new(SizeType.Absolute,135));fields.ColumnStyles.Add(new(SizeType.Percent,100));
        Control Field(string key,string value,int max,bool multiline=false)
        {
            int row=fields.RowCount++;fields.RowStyles.Add(new(SizeType.AutoSize));fields.Controls.Add(new Label{Text=AssetCsv.Caption(key),AutoSize=true,Padding=new Padding(0,5,0,0)},0,row);
            Control input=AssetCatalog.Kinds.Contains(key)?CatalogInput(key,value):new TextBox{Name=key,Text=value,MaxLength=max,Dock=DockStyle.Top,Multiline=multiline,Height=multiline?88:26,ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None};fields.Controls.Add(input,1,row);return input;
        }
        var type=Field("AssetType",source.AssetType,80);var brand=Field("Brand",source.Brand,80);var model=Field("Model",source.Model,100);var serial=Field("Serial",source.Serial,120);var part=Field("PartNumber",source.PartNumber,100);var mac=Field("MacAddress",source.MacAddress,100);
        int statusRow=fields.RowCount++;fields.RowStyles.Add(new(SizeType.AutoSize));fields.Controls.Add(new Label{Text=AssetCsv.Caption("Status"),AutoSize=true},0,statusRow);var state=new ComboBox{Name="Status",Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,Tag="UserData"};foreach(string key in AssetStore.Statuses)state.Items.Add(new StatusChoice(key,AssetStore.StatusCaption(key)));state.SelectedIndex=Array.IndexOf(AssetStore.Statuses,source.Status);fields.Controls.Add(state,1,statusRow);
        var location=Field("Location",source.Location,160);var custodian=Field("Custodian",source.Custodian,160);var notes=Field("Notes",source.Notes,2000,true);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,Padding=new Padding(12,4,0,0)};var save=new Button{Name="SaveAsset",Text=Ui.T("Guardar activo","Save asset"),AutoSize=true};var cancel=new Button{Text=Ui.T("Cancelar","Cancel"),AutoSize=true,DialogResult=DialogResult.Cancel};footer.Controls.AddRange([save,cancel]);dialog.Controls.Add(fields);dialog.Controls.Add(footer);dialog.CancelButton=cancel;
        save.Click+=(_,_)=>Run(()=>{store.Save(source with {AssetType=type.Text,Brand=brand.Text,Model=model.Text,Serial=serial.Text,PartNumber=part.Text,MacAddress=mac.Text,Status=((StatusChoice)state.SelectedItem!).Key,Location=location.Text,Custodian=custodian.Text,Notes=notes.Text});dialog.DialogResult=DialogResult.OK;});FluentIcons.ApplyTree(dialog);return dialog;
    }
    /// <summary>Shows field changes and optional workflow reasons, preserving local dates and account attribution.</summary>
    private void ShowMovements()
    {
        var item=Selected();using var dialog=new AppWindow{Text=Ui.T("Movimientos · ","Movements · ")+item.Code,Width=1000,Height=560,StartPosition=FormStartPosition.CenterParent};
        using var data=new DataTable();foreach(string label in new[]{Ui.T("Fecha","Date"),Ui.T("Acción","Action"),Ui.T("Campo","Field"),Ui.T("Antes","Before"),Ui.T("Después","After"),Ui.T("Usuario","User")})data.Columns.Add(label);
        foreach(var movement in store.Movements(item.Uid))
        {
            string action=movement.Action switch{"Migrated"=>Ui.T("Desde historial","From history"),"Registered"=>Ui.T("Registrado al etiquetar","Registered from label"),"Created"=>Ui.T("Creado","Created"),"Imported"=>Ui.T("Importado","Imported"),"ImportUpdated"=>Ui.T("Actualizado por CSV","Updated from CSV"),"Assigned"=>Ui.T("Asignado","Assigned"),"Returned"=>Ui.T("Devuelto","Returned"),"RepairStarted"=>Ui.T("Enviado a reparación","Sent to repair"),_=>Ui.T("Modificado","Updated")};
            void Row(string field,string before,string after)=>data.Rows.Add(DateTimeOffset.Parse(movement.AtUtc,CultureInfo.InvariantCulture).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),action,field,before,after,movement.Actor);
            foreach(var change in AssetCsv.Changes(new(0,movement.Before,movement.After)))Row(AssetCsv.Caption(change.Field),change.Field=="Status"&&change.Before.Length>0?AssetStore.StatusCaption(change.Before):change.Before,change.Field=="Status"?AssetStore.StatusCaption(change.After):change.After);
            if(movement.Note.Length>0)Row(Ui.T("Motivo","Reason"),"",movement.Note);
        }
        var view=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,DataSource=data,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White};dialog.Controls.Add(view);dialog.ShowDialog(FindForm());
    }
    /// <summary>Reports recoverable errors; verification rethrows instead of hanging on a modal warning.</summary>
    private void Run(Action action){try{action();}catch(Exception error){if(Environment.GetCommandLineArgs().Contains("--self-test"))throw;MessageBox.Show(FindForm(),error.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    /// <summary>Disposes only the owned projection before child controls.</summary>
    protected override void Dispose(bool disposing){if(disposing)table?.Dispose();base.Dispose(disposing);}
}
