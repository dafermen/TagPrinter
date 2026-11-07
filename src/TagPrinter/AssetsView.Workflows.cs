using System.Data;
namespace TagPrinter;

internal sealed partial class AssetsView
{
    /// <summary>Shows a guided action dialog and refreshes only after explicit successful confirmation.</summary>
    private void ShowMovementAction(string action){using var dialog=CreateMovement(Selected(),action);if(dialog.ShowDialog(FindForm())==DialogResult.OK)RefreshData();}
    /// <summary>Builds the actual assignment/return/repair form, including destination and an audit-only reason.</summary>
    internal Form CreateMovement(AssetRecord source,string action)
    {
        string title=action switch{"Assigned"=>Ui.T("Asignar activo","Assign asset"),"Returned"=>Ui.T("Devolver activo","Return asset"),"RepairStarted"=>Ui.T("Enviar a reparación","Send to repair"),_=>throw new ArgumentException("Unknown movement.")};
        var dialog=new AppWindow{Text=title+" · "+source.Code,Width=600,Height=410,MinimumSize=new Size(500,390),StartPosition=FormStartPosition.CenterParent};
        var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(16)};fields.ColumnStyles.Add(new(SizeType.Percent,100));
        fields.Controls.Add(new Label{AutoSize=true,Text=source.AssetType+" · "+source.Brand+" · "+source.Model+" · "+source.Serial});fields.Controls.Add(new Label{AutoSize=true,Text=Ui.T("Ubicación de destino (requerida)","Destination location (required)")});var location=CatalogInput("Location",source.Location);fields.Controls.Add(location);
        fields.Controls.Add(new Label{AutoSize=true,Text=action=="Assigned"?Ui.T("Responsable (requerido)","Custodian (required)"):Ui.T("Responsable / servicio técnico","Custodian / service provider")});var custodian=CatalogInput("Custodian",action=="Returned"?"":source.Custodian);custodian.Enabled=action!="Returned";fields.Controls.Add(custodian);
        fields.Controls.Add(new Label{AutoSize=true,Text=Ui.T("Motivo del movimiento (opcional)","Movement reason (optional)")});var reason=new TextBox{Name="MovementReason",Multiline=true,ScrollBars=ScrollBars.Vertical,Height=85,MaxLength=1000,Dock=DockStyle.Top};fields.Controls.Add(reason);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,Padding=new Padding(12,4,0,0)};var confirm=new Button{Name="ConfirmMovement",Text=title,AutoSize=true};var cancel=new Button{Text=Ui.T("Cancelar","Cancel"),AutoSize=true,DialogResult=DialogResult.Cancel};footer.Controls.AddRange([confirm,cancel]);dialog.Controls.Add(fields);dialog.Controls.Add(footer);dialog.CancelButton=cancel;
        confirm.Click+=(_,_)=>Run(()=>{store.Transition(source,action,location.Text,custodian.Text,reason.Text);dialog.DialogResult=DialogResult.OK;});FluentIcons.ApplyTree(dialog);return dialog;
    }
    /// <summary>Maintains reusable options independently of creating a physical asset.</summary>
    private void ShowCatalogs(){using var dialog=CreateCatalogEditor();dialog.ShowDialog(FindForm());}
    /// <summary>Adds case/trim-unique user vocabulary; existing asset/history values are never bulk-rewritten.</summary>
    internal Form CreateCatalogEditor()
    {
        var catalog=new AssetCatalog(history);var dialog=new AppWindow{Text=Ui.T("Catálogos de activos","Asset catalogs"),Width=620,Height=500,StartPosition=FormStartPosition.CenterParent};
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(16)};root.ColumnStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,36));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,48));
        var kind=new ComboBox{Name="CatalogKind",Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,Tag="UserData"};foreach(string key in AssetCatalog.Kinds)kind.Items.Add(new StatusChoice(key,AssetCsv.Caption(key)));var values=new ListBox{Dock=DockStyle.Fill};var footer=new FlowLayoutPanel{Dock=DockStyle.Fill};var input=new TextBox{Name="CatalogValue",Width=350,MaxLength=160};var add=new Button{Name="AddCatalogValue",Text=Ui.T("Agregar","Add value"),AutoSize=true};footer.Controls.AddRange([input,add]);root.Controls.Add(kind,0,0);root.Controls.Add(values,0,1);root.Controls.Add(footer,0,2);dialog.Controls.Add(root);
        void Refresh(){values.Items.Clear();values.Items.AddRange(catalog.Values(((StatusChoice)kind.SelectedItem!).Key).Cast<object>().ToArray());}
        kind.SelectedIndexChanged+=(_,_)=>Refresh();kind.SelectedIndex=0;add.Click+=(_,_)=>Run(()=>{string name=catalog.Add(((StatusChoice)kind.SelectedItem!).Key,input.Text);Refresh();values.SelectedItem=name;input.Clear();});FluentIcons.ApplyTree(dialog);return dialog;
    }
    /// <summary>Exports all matching master records across pages, with identity and optimistic revision metadata.</summary>
    private void ExportAssets()
    {
        using var dialog=new SaveFileDialog{Filter="CSV|*.csv",DefaultExt="csv",AddExtension=true,FileName="TagPrinter-assets.csv"};if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;store.ExportCsv(dialog.FileName,CurrentFilter);
    }
    /// <summary>Writes the full administrative schema; blank identity/revision cells designate a new asset.</summary>
    private void SaveAssetTemplate()
    {
        using var dialog=new SaveFileDialog{Filter="CSV|*.csv",DefaultExt="csv",AddExtension=true,FileName="TagPrinter-assets-template.csv"};if(dialog.ShowDialog(FindForm())==DialogResult.OK)AssetCsv.Export(dialog.FileName,[new AssetRecord{AssetType="Monitor",Brand="Dell",Model="H22203H",Serial="000123",Status="Available",Location="Storage"}]);
    }
    /// <summary>Validates everything before preview; backup and the all-or-nothing write happen only after confirmation.</summary>
    private void ImportAssets()
    {
        using var file=new OpenFileDialog{Filter="CSV|*.csv"};if(file.ShowDialog(FindForm())!=DialogResult.OK)return;var plan=AssetCsv.Read(file.FileName,store);
        using var preview=CreateImportPreview(plan);if(preview.ShowDialog(FindForm())!=DialogResult.OK)return;
        string backups=Path.Combine(Path.GetDirectoryName(history.DatabasePath)!,"backups");Directory.CreateDirectory(backups);history.Backup(Path.Combine(backups,"TagPrinter-before-asset-import-"+Guid.NewGuid().ToString("N")+".db"));
        var result=store.ImportAssets(plan);RefreshData();MessageBox.Show(FindForm(),string.Format(Ui.T("Creados: {0}. Actualizados: {1}. Sin cambios: {2}.","Created: {0}. Updated: {1}. Unchanged: {2}."),result.Created,result.Updated,result.Unchanged),"TagPrinter");
    }
    /// <summary>Shows field-by-field before/after values; caps preview rendering at 200 assets, while all rows remain validated.</summary>
    internal Form CreateImportPreview(IReadOnlyList<AssetImportRow> plan)
    {
        var dialog=new AppWindow{Text=Ui.T("Revisar importación de activos","Review asset import"),Width=1100,Height=650,StartPosition=FormStartPosition.CenterParent};var data=new DataTable();foreach(string key in new[]{"Record","Action","Code","Serial","Field","Before","After"})data.Columns.Add(key);
        foreach(var row in plan.Take(200))
        {
            var changes=AssetCsv.Changes(row).ToList();string action=row.Before is null?Ui.T("Crear","Create"):changes.Count==0?Ui.T("Sin cambios","Unchanged"):Ui.T("Actualizar","Update");
            if(changes.Count==0)data.Rows.Add(row.Record,action,row.Before?.Code??"",row.After.Serial,"—","","");
            foreach(var change in changes)data.Rows.Add(row.Record,action,row.Before?.Code??"",row.After.Serial,AssetCsv.Caption(change.Field),change.Field=="Status"&&change.Before.Length>0?AssetStore.StatusCaption(change.Before):change.Before,change.Field=="Status"?AssetStore.StatusCaption(change.After):change.After);
        }
        var grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,DataSource=data,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White};grid.DataBindingComplete+=(_,_)=>{string[] captions=[Ui.T("Registro","Record"),Ui.T("Acción","Action"),Ui.T("Código","Code"),"Serial",Ui.T("Campo","Field"),Ui.T("Antes","Before"),Ui.T("Después","After")];for(int i=0;i<captions.Length;i++)grid.Columns[i].HeaderText=captions[i];};
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=125,Padding=new Padding(8)};var text=new Label{AutoSize=true,MaximumSize=new Size(1030,0),Text=string.Format(Ui.T("{0} activos validados. Vista previa: primeros {1}. Se actualizarán los campos mostrados, incluidos valores vacíos. Se guardará un respaldo previo. No se imprimirá.","{0} validated assets. Preview: first {1}. Shown fields, including blank values, will be updated. A safety backup will be saved. Nothing will print."),plan.Count,Math.Min(200,plan.Count))};footer.Controls.Add(text);footer.SetFlowBreak(text,true);
        int creates=plan.Count(r=>r.Before is null),updates=plan.Count(r=>r.Before is not null && AssetCsv.Changes(r).Any());var totals=new Label{AutoSize=true,Text=string.Format(Ui.T("Crear: {0} · Actualizar: {1} · Sin cambios: {2}","Create: {0} · Update: {1} · Unchanged: {2}"),creates,updates,plan.Count-creates-updates)};footer.Controls.Add(totals);footer.SetFlowBreak(totals,true);
        footer.Controls.Add(new Button{Name="ConfirmAssetImport",Text=Ui.T("Confirmar importación","Confirm import"),AutoSize=true,DialogResult=DialogResult.OK});var cancel=new Button{Text=Ui.T("Cancelar","Cancel"),AutoSize=true,DialogResult=DialogResult.Cancel};footer.Controls.Add(cancel);dialog.CancelButton=cancel;dialog.Controls.Add(grid);dialog.Controls.Add(footer);dialog.Disposed+=(_,_)=>data.Dispose();FluentIcons.ApplyTree(dialog);return dialog;
    }
}
