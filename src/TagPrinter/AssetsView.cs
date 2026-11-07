using System.Data;
using System.Globalization;
namespace TagPrinter;

/// <summary>Local canonical asset administration: paged records, explicit editing, retirement, audit and label loading.</summary>
internal sealed class AssetsView : UserControl
{
    // Store owns persistence; the view owns only temporary controls/tables and current navigation criteria.
    private readonly AssetStore store;
    private readonly Action<AssetRecord> loadLabel;
    private readonly TextBox search=new(){Width=230};
    private readonly ComboBox status=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=160};
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=true,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,RowHeadersVisible=false,BackgroundColor=Color.White};
    private readonly Label summary=new(){Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0)};
    private readonly List<(Button Button,string Es,string En)> captions=[];
    private readonly Label pageLabel=new(){AutoSize=true,Padding=new Padding(4,7,4,0)};
    private readonly Button previous=new(){Text="◀",Width=42},next=new(){Text="▶",Width=42};
    private int page;
    private string sortKey="Updated";
    private bool sortDescending=true;
    // Dashboard drill-down supplies an exact group; this is separate from the user's literal search text.
    private string? groupType,groupBrand,groupModel;
    private bool translating;
    private DataTable? table;
    private sealed record StatusChoice(string Key,string Caption){public override string ToString()=>Caption;}
    /// <summary>Builds bounded list navigation and callbacks; no command prints automatically.</summary>
    internal AssetsView(HistoryStore history,Action<AssetRecord> onLoad)
    {
        store=new AssetStore(history);loadLabel=onLoad;Dock=DockStyle.Fill;Padding=new Padding(16);BackColor=Color.White;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};root.RowStyles.Add(new RowStyle(SizeType.Absolute,112));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill};toolbar.Controls.Add(search);toolbar.Controls.Add(status);
        Button Add(string es,string en,Action action){var b=new Button{AutoSize=true};captions.Add((b,es,en));b.Click+=(_,_)=>Run(action);toolbar.Controls.Add(b);return b;}
        Add("Buscar / actualizar","Search / refresh",()=>{page=0;RefreshData();});Add("Limpiar filtros","Clear filters",()=>{groupType=groupBrand=groupModel=null;search.Clear();status.SelectedIndex=0;page=0;RefreshData();});Add("Nuevo activo","New asset",()=>Edit(new()));Add("Editar","Edit",()=>Edit(Selected()));
        Add("Duplicar","Duplicate",()=>Edit(Selected() with {Uid="",Number=0,Revision=0,Serial="",PartNumber="",MacAddress="",Status="Available",Custodian="",Location="",Notes="",CreatedUtc="",UpdatedUtc=""}));
        Add("Dar de baja","Retire",()=>{var item=Selected();if(MessageBox.Show(FindForm(),Ui.T("¿Dar de baja este activo? Su ficha e historial se conservarán.","Retire this asset? Its record and history will be preserved."),"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){store.Save(item with {Status="Retired"});RefreshData();}});
        Add("Cargar etiqueta","Load label",()=>loadLabel(Selected()));Add("Movimientos","Movements",ShowMovements);
        previous.Click+=(_,_)=>{if(page>0){page--;RefreshData();}};next.Click+=(_,_)=>{page++;RefreshData();};toolbar.Controls.AddRange([previous,pageLabel,next]);
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;page=0;Run(RefreshData);}};status.SelectedIndexChanged+=(_,_)=>{if(!translating){page=0;Run(RefreshData);}};
        grid.ColumnHeaderMouseClick+=(_,e)=>{if(e.ColumnIndex<0)return;string key=grid.Columns[e.ColumnIndex].Name;sortDescending=key==sortKey?!sortDescending:false;sortKey=key;page=0;Run(RefreshData);};
        grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)Run(()=>Edit(Selected()));};root.Controls.Add(toolbar,0,0);root.Controls.Add(grid,0,1);root.Controls.Add(summary,0,2);Controls.Add(root);RefreshLanguage();
    }
    /// <summary>Refreshes translated captions and states while preserving the selected status key.</summary>
    internal void RefreshLanguage()
    {
        search.PlaceholderText=Ui.T("Buscar código, serial o responsable…","Search code, serial or custodian…");
        translating=true;string key=(status.SelectedItem as StatusChoice)?.Key??"";
        foreach(var item in captions)item.Button.Text=Ui.T(item.Es,item.En);
        status.Items.Clear();status.Items.Add(new StatusChoice("",Ui.T("Todos los estados","All statuses")));foreach(string state in AssetStore.Statuses)status.Items.Add(new StatusChoice(state,AssetStore.StatusCaption(state)));status.SelectedIndex=Math.Max(0,status.Items.Cast<StatusChoice>().ToList().FindIndex(s=>s.Key==key));translating=false;
        FluentIcons.ApplyTree(this);RefreshData();
    }
    /// <summary>Reads one bounded SQL page. Retired assets stay available under the status filter.</summary>
    internal void RefreshData()
    {
        string key=(status.SelectedItem as StatusChoice)?.Key??"";long total=store.Count(search.Text.Trim(),key,groupType,groupBrand,groupModel);page=(int)Math.Min(page,Math.Max(0,(total-1)/50));var rows=store.Query(search.Text.Trim(),key,page*50,50,groupType,groupBrand,groupModel,sortKey,sortDescending);
        var data=new DataTable();foreach(string name in new[]{"Uid","Code","AssetType","Brand","Model","Serial","Status","Location","Custodian","PartNumber","MacAddress"})data.Columns.Add(name);data.Columns.Add("Updated",typeof(DateTime));
        foreach(var row in rows)data.Rows.Add(row.Uid,row.Code,row.AssetType,row.Brand,row.Model,row.Serial,AssetStore.StatusCaption(row.Status),row.Location,row.Custodian,row.PartNumber,row.MacAddress,DateTimeOffset.Parse(row.UpdatedUtc,CultureInfo.InvariantCulture).LocalDateTime);
        var old=table;table=data;grid.DataSource=data;old?.Dispose();grid.Columns["Uid"].Visible=false;
        string[] labels=["Uid",Ui.T("Código interno","Asset code"),Ui.T("Tipo de activo","Asset type"),Ui.T("Marca","Brand"),Ui.T("Modelo","Model"),"Serial",Ui.T("Estado","Status"),Ui.T("Ubicación","Location"),Ui.T("Responsable","Custodian"),Ui.T("No. de parte","Part number"),"MAC",Ui.T("Actualizado","Updated")];
        for(int i=0;i<labels.Length;i++){grid.Columns[i].HeaderText=labels[i];grid.Columns[i].Width=i==11||i==6?155:125;grid.Columns[i].SortMode=DataGridViewColumnSortMode.Programmatic;grid.Columns[i].HeaderCell.SortGlyphDirection=grid.Columns[i].Name==sortKey?(sortDescending?SortOrder.Descending:SortOrder.Ascending):SortOrder.None;}grid.Columns["Updated"].DefaultCellStyle.Format="yyyy-MM-dd HH:mm:ss";
        pageLabel.Text=$"{page+1} / {Math.Max(1,(total+49)/50)}";previous.Enabled=page>0;next.Enabled=(page+1)*50<total;
        summary.Text=string.Format(Ui.T("{0} activos. Una ficha por activo; las reimpresiones no crean otra ficha.\nDoble clic para editar. Cargar etiqueta no imprime automáticamente.","{0} assets. One record per asset; reprints do not create another asset.\nDouble-click to edit. Load label does not print automatically."),total).Replace("\n",Environment.NewLine);
    }
    /// <summary>Shows the full selected canonical group; Clear filters leaves group mode.</summary>
    internal void ShowGroup(string type,string brand,string model){search.Clear();groupType=type;groupBrand=brand;groupModel=model;status.SelectedIndex=0;page=0;RefreshData();}
    /// <summary>Loads a fresh record by hidden stable UID rather than trusting displayed text or row order.</summary>
    private AssetRecord Selected()
    {
        if(grid.CurrentRow is null)throw new InvalidOperationException(Ui.T("Selecciona un activo.","Select an asset."));
        return store.Get(grid.CurrentRow.Cells["Uid"].Value.ToString()!)??throw new InvalidDataException("Asset not found.");
    }
    /// <summary>Edits business fields explicitly; failed validation keeps the dialog and entered data open.</summary>
    private void Edit(AssetRecord source)
    {
        using var dialog=CreateEditor(source);if(dialog.ShowDialog(FindForm())==DialogResult.OK)RefreshData();
    }
    /// <summary>Builds the real editor without showing/saving it, allowing isolated UI tests of field binding.</summary>
    internal Form CreateEditor(AssetRecord source)
    {
        var dialog=new AppWindow{Text=source.Uid.Length==0?Ui.T("Nuevo activo","New asset"):source.Code,Width=640,Height=640,MinimumSize=new Size(540,600),StartPosition=FormStartPosition.CenterParent};
        var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,AutoScroll=true,Padding=new Padding(16)};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,135));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        TextBox Field(string caption,string value,int max,bool multiline=false){int row=fields.RowCount++;fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));fields.Controls.Add(new Label{Text=caption,AutoSize=true,Padding=new Padding(0,5,0,0)},0,row);var text=new TextBox{Text=value,MaxLength=max,Dock=DockStyle.Top,Multiline=multiline,Height=multiline?88:26,ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None};fields.Controls.Add(text,1,row);return text;}
        var type=Field(Ui.T("Tipo de activo","Asset type"),source.AssetType,80);var brand=Field(Ui.T("Marca","Brand"),source.Brand,80);var model=Field(Ui.T("Modelo","Model"),source.Model,100);var serial=Field("Serial",source.Serial,120);var part=Field(Ui.T("No. de parte","Part number"),source.PartNumber,100);var mac=Field("MAC",source.MacAddress,100);
        int rowIndex=fields.RowCount++;fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));fields.Controls.Add(new Label{Text=Ui.T("Estado","Status"),AutoSize=true},0,rowIndex);var state=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};foreach(string key in AssetStore.Statuses)state.Items.Add(new StatusChoice(key,AssetStore.StatusCaption(key)));state.SelectedIndex=Array.IndexOf(AssetStore.Statuses,source.Status);fields.Controls.Add(state,1,rowIndex);
        var location=Field(Ui.T("Ubicación / sede / área","Location / site / area"),source.Location,160);var custodian=Field(Ui.T("Responsable","Custodian"),source.Custodian,160);var notes=Field(Ui.T("Observaciones","Notes"),source.Notes,2000,true);
        type.Name="AssetType";brand.Name="Brand";model.Name="Model";serial.Name="Serial";part.Name="PartNumber";mac.Name="MacAddress";state.Name="Status";location.Name="Location";custodian.Name="Custodian";notes.Name="Notes";
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,Padding=new Padding(12,4,0,0)};var save=new Button{Name="SaveAsset",Text=Ui.T("Guardar activo","Save asset"),AutoSize=true};var cancel=new Button{Text=Ui.T("Cancelar","Cancel"),AutoSize=true,DialogResult=DialogResult.Cancel};footer.Controls.AddRange([save,cancel]);dialog.Controls.Add(fields);dialog.Controls.Add(footer);dialog.CancelButton=cancel;
        save.Click+=(_,_)=>{try{store.Save(source with {AssetType=type.Text,Brand=brand.Text,Model=model.Text,Serial=serial.Text,PartNumber=part.Text,MacAddress=mac.Text,Status=((StatusChoice)state.SelectedItem!).Key,Location=location.Text,Custodian=custodian.Text,Notes=notes.Text});dialog.DialogResult=DialogResult.OK;}catch(Exception error){MessageBox.Show(dialog,error.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};
        FluentIcons.ApplyTree(dialog);return dialog;
    }
    /// <summary>Displays field-level changes, dates and local account attribution without exposing JSON implementation details.</summary>
    private void ShowMovements()
    {
        var item=Selected();using var dialog=new AppWindow{Text=Ui.T("Movimientos · ","Movements · ")+item.Code,Width=1000,Height=560,StartPosition=FormStartPosition.CenterParent};
        var data=new DataTable();foreach(string label in new[]{Ui.T("Fecha","Date"),Ui.T("Acción","Action"),Ui.T("Campo","Field"),Ui.T("Antes","Before"),Ui.T("Después","After"),Ui.T("Usuario","User")})data.Columns.Add(label);
        var labels=new[]{("AssetType",Ui.T("Tipo de activo","Asset type")),("Brand",Ui.T("Marca","Brand")),("Model",Ui.T("Modelo","Model")),("Serial","Serial"),("PartNumber",Ui.T("No. de parte","Part number")),("MacAddress","MAC"),("Status",Ui.T("Estado","Status")),("Location",Ui.T("Ubicación","Location")),("Custodian",Ui.T("Responsable","Custodian")),("Notes",Ui.T("Observaciones","Notes"))};
        foreach(var movement in store.Movements(item.Uid))foreach(var field in labels)
        {
            var property=typeof(AssetRecord).GetProperty(field.Item1)!;string before=movement.Before is null?"":property.GetValue(movement.Before)?.ToString()??"",after=property.GetValue(movement.After)?.ToString()??"";if(before==after)continue;
            if(field.Item1=="Status"){if(before.Length>0)before=AssetStore.StatusCaption(before);after=AssetStore.StatusCaption(after);}
            string action=movement.Action switch{"Migrated"=>Ui.T("Desde historial","From history"),"Registered"=>Ui.T("Registrado al etiquetar","Registered from label"),"Created"=>Ui.T("Creado","Created"),"Imported"=>Ui.T("Importado","Imported"),_=>Ui.T("Modificado","Updated")};
            data.Rows.Add(DateTimeOffset.Parse(movement.AtUtc,CultureInfo.InvariantCulture).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),action,field.Item2,before,after,movement.Actor);
        }
        var view=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,DataSource=data,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White};dialog.Controls.Add(view);dialog.ShowDialog(FindForm());data.Dispose();
    }
    /// <summary>Keeps recoverable command failures readable; self-tests rethrow so failures cannot hide in a modal dialog.</summary>
    private void Run(Action action){try{action();}catch(Exception e){if(Environment.GetCommandLineArgs().Contains("--self-test"))throw;MessageBox.Show(FindForm(),e.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    /// <summary>Releases the owned projection before child controls are disposed.</summary>
    protected override void Dispose(bool disposing){if(disposing)table?.Dispose();base.Dispose(disposing);}
}
