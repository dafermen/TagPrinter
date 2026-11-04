using System.Data;
namespace TagPrinter;

/// <summary>
/// Displays sortable events, filters, CSV, backup and reversible deletion. Row loading delegates to the
/// form without printing.
/// </summary>
public sealed class ReportsView : UserControl
{
    // current keeps original snapshots; table contains translated display values. The load callback
    // navigates to the editor.
    readonly HistoryStore store;
    // load hands selected snapshots back to MainForm; this view never calls the printer.
    readonly Action<HistoryEvent> load;
    // search contains literal partial text. field indexes map to Fields keys; action indexes map to stable
    // event codes.
    readonly TextBox search=new(){Width=220};
    // Selected indexes map to Fields keys and HistoryStore.Actions, not translated stored values.
    readonly ComboBox field=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=145}, action=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=165};
    // from/to filters are enabled only when checked; displayed dates use the local calendar.
    readonly DateTimePicker from=new(){Format=DateTimePickerFormat.Short,ShowCheckBox=true,Checked=false,Width=125,Value=DateTime.Today.AddDays(-7)}, to=new(){Format=DateTimePickerFormat.Short,ShowCheckBox=true,Checked=false,Width=125};
    // Caption controls and count are presentation-only and may be translated without changing stored events.
    readonly Label searchLabel=new(){AutoSize=true}, fromLabel=new(){AutoSize=true}, toLabel=new(){AutoSize=true}, count=new(){Dock=DockStyle.Fill,AutoSize=false};
    // apply refreshes, reset clears filters, export/exportAll choose scope, backup copies SQLite, open loads
    // the selected label.
    readonly Button apply=new(){AutoSize=true}, reset=new(){AutoSize=true}, export=new(){AutoSize=true}, exportAll=new(){AutoSize=true}, backup=new(){AutoSize=true}, open=new(){AutoSize=true};
    // Grid supports multi-row selection and sorting; hidden columns are reachable by horizontal scrolling.
    internal readonly DataGridView Grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=true,BackgroundColor=Color.White,RowHeadersVisible=false};
    // trash switches between active/recycled event scopes. remove means Delete in active scope and Restore
    // in trash scope.
    readonly CheckBox trash=new(){AutoSize=true};
    // Deletes in active scope and restores in recycle-bin scope.
    readonly Button remove=new(){AutoSize=true};
    // current is the original queried list; table is its typed/translatable projection and owns the current
    // sort/filter.
    // Purge is explicit and irreversible; the callback invalidates any currently loaded source reference.
    readonly Button emptyTrash=new(){AutoSize=true};
    readonly Action? onTrashEmptied;
    List<HistoryEvent> current=[];
    // Owned typed projection; its DefaultView retains sorting and group filtering.
    DataTable? table;
    // Stable search keys in the same order as the field selector; these are SQL-whitelisted downstream.
    static readonly string[] Fields=["all","serial","asset_type","brand","model","part_number","mac_address"];
    /// <summary>
    /// Builds filters/table/actions; row activation delegates to the loader and delete/restore requires
    /// confirmation.
    /// </summary>
    /// <param name="history">History store; tests supply an isolated instance.</param>
    /// <param name="onLoad">Callback loading a selected event into the editor without printing.</param>
    /// <param name="onTrashEmptied">Optional callback after permanent deletion, used to clear a loaded source that no longer exists.</param>
    public ReportsView(HistoryStore history,Action<HistoryEvent> onLoad,Action? onTrashEmptied=null)
    {
        store=history;load=onLoad;this.onTrashEmptied=onTrashEmptied;BackColor=Color.White;Dock=DockStyle.Fill;Padding=new Padding(16);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,88));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        filters.Controls.AddRange([searchLabel,search,field,action,fromLabel,from,toLabel,to,apply,reset]);
        layout.Controls.Add(filters,0,0);layout.Controls.Add(Grid,0,1);layout.Controls.Add(count,0,2);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};buttons.Controls.AddRange([open,export,exportAll,backup,trash,remove,emptyTrash]);layout.Controls.Add(buttons,0,3);Controls.Add(layout);
        // Adds an explicitly bound sortable column; serial cells use links.
        void Column(string name,int width,bool link=false)
        {
            DataGridViewColumn column=link?new DataGridViewLinkColumn{TrackVisitedState=false}:new DataGridViewTextBoxColumn();
            column.Name=name;column.DataPropertyName=name;column.Width=width;column.SortMode=DataGridViewColumnSortMode.Automatic;Grid.Columns.Add(column);
        }
        Column("Id",55);Column("Date",150);Column("Action",150);Column("Serial",145,true);Column("AssetType",105);Column("Brand",90);Column("Model",120);Column("PartNumber",120);Column("MacAddress",155);Column("Printer",190);Column("Copies",65);Column("CodeType",80);Column("SourceEventId",90);
        Grid.Columns["Date"].DefaultCellStyle.Format="yyyy-MM-dd HH:mm:ss";
        apply.Click+=(_,_)=>Run(RefreshData);reset.Click+=(_,_)=>{search.Clear();field.SelectedIndex=0;action.SelectedIndex=0;from.Checked=false;to.Checked=false;Run(RefreshData);};
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Run(RefreshData);}};
        Grid.CellContentClick+=(_,e)=>{if(e.RowIndex>=0 && e.ColumnIndex>=0 && Grid.Columns[e.ColumnIndex].Name=="Serial")Run(()=>OpenRow(e.RowIndex));};
        open.Click+=(_,_)=>Run(()=>OpenRow(Grid.CurrentRow?.Index??-1));
        export.Click+=(_,_)=>Run(()=>Export(false));exportAll.Click+=(_,_)=>Run(()=>Export(true));backup.Click+=(_,_)=>Run(Backup);
        trash.CheckedChanged+=(_,_)=>Run(()=>{RefreshLanguage();});
        // Resolve selected rows to stable IDs, confirm and change membership transactionally. Files and
        // existing printer jobs are unaffected.
        remove.Click+=(_,_)=>Run(()=>{
            var ids=Grid.SelectedRows.Cast<DataGridViewRow>().Select(r=>Convert.ToInt64(r.Cells["Id"].Value)).ToArray();
            if(ids.Length==0)return;
            string message=trash.Checked?Ui.T("¿Restaurar los registros seleccionados?","Restore selected records?"):Ui.T("¿Mover los registros seleccionados a la papelera?","Move selected records to the recycle bin?");
            if(MessageBox.Show(FindForm(),message,"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            store.SetDeleted(ids,!trash.Checked);RefreshData();
        });
        emptyTrash.Click+=(_,_)=>Run(()=>{
            int total=store.RecycleBinCount();if(total==0)return;
            string warning=string.Format(Ui.T("¿Eliminar definitivamente TODOS los {0} registros de la papelera? Incluye los ocultos por filtros. Esta acción no se puede deshacer.","Permanently delete ALL {0} recycled records, including those hidden by filters? This cannot be undone."),total);
            if(MessageBox.Show(FindForm(),warning,"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)PurgeRecycleBin();
        });
        field.Items.AddRange(new object[Fields.Length].Select(_=>(object)"").ToArray());field.SelectedIndex=0;
        action.Items.AddRange(new object[HistoryStore.Actions.Length+1].Select(_=>(object)"").ToArray());action.SelectedIndex=0;
        from.Checked=false;to.Checked=false;RefreshLanguage();
    }
    /// <summary>Maps stable action codes to display captions without changing stored values.</summary>
    /// <param name="value">Text to sanitize/translate, or initial numeric editor value, according to the signature.</param>
    /// <returns>Localized action caption.</returns>
    static string ActionText(string value)=>value switch
    {
        "ExportTag"=>Ui.T("Etiqueta exportada","Tag exported"),"ExportCode"=>Ui.T("Código exportado","Code exported"),"ExportZpl"=>Ui.T("ZPL exportado","ZPL exported"),"PrintSubmitted"=>Ui.T("Enviado a impresión","Sent to printer"),_=>value
    };
    /// <summary>
    /// Translates captions/choices while retaining indexes, then reloads rows with existing date filters.
    /// </summary>
    public void RefreshLanguage()
    {
        emptyTrash.Text=Ui.T("Vaciar papelera…","Empty recycle bin…");
        trash.Text=Ui.T("Papelera","Recycle bin");remove.Text=trash.Checked?Ui.T("Restaurar","Restore"):Ui.T("Eliminar","Delete");
        searchLabel.Text=Ui.T("Buscar","Search");fromLabel.Text=Ui.T("Desde","From");toLabel.Text=Ui.T("Hasta","To");
        apply.Text=Ui.T("Buscar / actualizar","Search / refresh");reset.Text=Ui.T("Limpiar filtros","Clear filters");open.Text=Ui.T("Cargar etiqueta","Load label");
        export.Text=Ui.T("Exportar vista CSV","Export view CSV");exportAll.Text=Ui.T("Exportar todo CSV","Export all CSV");backup.Text=Ui.T("Copia de seguridad","Back up database");
        int fi=Math.Max(0,field.SelectedIndex),ai=Math.Max(0,action.SelectedIndex);
        string[] names=[Ui.T("Todos los campos","All fields"),"Serial",Ui.T("Tipo de activo","Asset type"),Ui.T("Marca","Brand"),Ui.T("Modelo","Model"),Ui.T("No. de parte","Part number"),Ui.T("Dirección MAC","MAC address")];
        field.Items.Clear();field.Items.AddRange(names);field.SelectedIndex=fi;
        action.Items.Clear();action.Items.Add(Ui.T("Todas las acciones","All actions"));foreach(var name in HistoryStore.Actions)action.Items.Add(ActionText(name));action.SelectedIndex=ai;
        string[] headers=["Id",Ui.T("Fecha / hora local","Local date / time"),Ui.T("Acción","Action"),"Serial",names[2],names[3],names[4],names[5],names[6],Ui.T("Impresora","Printer"),Ui.T("Copias","Copies"),Ui.T("Código","Code"),Ui.T("Registro origen","Source record")];
        for(int i=0;i<headers.Length;i++)Grid.Columns[i].HeaderText=headers[i];
        Run(RefreshData);
    }
    /// <summary>
    /// Converts optional local calendar dates to inclusive-start/exclusive-end UTC boundaries; rejects
    /// reversed dates.
    /// </summary>
    /// <returns>Combined text/action/date/trash criteria.</returns>
    HistoryQuery Filter()
    {
        if(from.Checked && to.Checked && from.Value.Date>to.Value.Date)throw new ArgumentException(Ui.T("La fecha inicial debe ser anterior o igual a la final.","Start date must not be after end date."));
        // Use a half-open UTC interval covering complete local days, including the To date. Each boundary
        // uses its local UTC offset.
        DateTimeOffset? start=from.Checked?new DateTimeOffset(DateTime.SpecifyKind(from.Value.Date,DateTimeKind.Local)).ToUniversalTime():null;
        DateTimeOffset? end=to.Checked?new DateTimeOffset(DateTime.SpecifyKind(to.Value.Date.AddDays(1),DateTimeKind.Local)).ToUniversalTime():null;
        return new(search.Text.Trim(),Fields[Math.Max(0,field.SelectedIndex)],action.SelectedIndex>0?HistoryStore.Actions[action.SelectedIndex-1]:"",start,end,trash.Checked);
    }
    /// <summary>Builds typed display rows from history, retains sorting and disposes the old table.</summary>
    public void RefreshData()
    {
        // Save sort before replacing typed data. Dates/numbers sort by value rather than their text representation.
        emptyTrash.Visible=trash.Checked;emptyTrash.Enabled=trash.Checked && store.RecycleBinCount()>0;
        current=store.Query(Filter());string sort=table?.DefaultView.Sort??"";
        var next=new DataTable();
        next.Columns.Add("Id",typeof(long));next.Columns.Add("Date",typeof(DateTime));foreach(var name in new[]{"Action","Serial","AssetType","Brand","Model","PartNumber","MacAddress","Printer"})next.Columns.Add(name,typeof(string));next.Columns.Add("Copies",typeof(int));next.Columns.Add("CodeType",typeof(string));next.Columns.Add("SourceEventId",typeof(long));
        foreach(var row in current){var state=row.State;next.Rows.Add(row.Id,row.CreatedUtc.LocalDateTime,ActionText(row.Action),string.IsNullOrWhiteSpace(state.Serial)?Ui.T("(Sin serial)","(No serial)"):state.Serial,state.AssetType,state.Brand,state.Model,state.PartNumber,state.MacAddress,state.Printer,state.Copies,!state.ShowCode && row.Action!="ExportCode"?Ui.T("Solo texto","Text only"):state.Code==1?"QR":"Code 128",(object?)row.SourceEventId??DBNull.Value);}
        if(sort.Length>0)next.DefaultView.Sort=sort;
        var old=table;table=next;Grid.DataSource=next;old?.Dispose();
        count.Text=string.Format(Ui.T("{0} registros. Pulsa un serial para cargar la etiqueta; no se imprime automáticamente.","{0} records. Click a serial to load its label; it will not print automatically."),current.Count);
    }
    /// <summary>
    /// Clears ordinary filters and applies exact type/brand/model DataView filtering to active history.
    /// </summary>
    /// <param name="type">Exact asset-type group value.</param>
    /// <param name="brand">Exact brand group value.</param>
    /// <param name="model">Exact model group value.</param>
    public void ShowGroup(string type,string brand,string model)
    {
        trash.Checked=false;search.Text="";field.SelectedIndex=0;action.SelectedIndex=0;from.Checked=false;to.Checked=false;
        // Escape quotes for the DataView expression. This exact group filter is separate from SQLite
        // substring search.
        RefreshData();table!.DefaultView.RowFilter=$"AssetType = '{type.Replace("'","''")}' AND Brand = '{brand.Replace("'","''")}' AND Model = '{model.Replace("'","''")}'";
    }
    /// <summary>Resolves displayed row to stable event ID and invokes the loader, independent of sorting.</summary>
    /// <param name="index">Display row index, resolved to stable ID before loading.</param>
    void OpenRow(int index)
    {
        if(index<0 || index>=Grid.Rows.Count)throw new InvalidOperationException(Ui.T("Selecciona un registro.","Select a record."));
        long id=Convert.ToInt64(Grid.Rows[index].Cells["Id"].Value);load(store.Get(id)??throw new InvalidDataException("Record not found."));
    }
    /// <summary>Maps sorted/filtered display rows to original event objects for faithful view export.</summary>
    /// <returns>Original events in visible order.</returns>
    internal List<HistoryEvent> VisibleRows()
    {
        var lookup=current.ToDictionary(row=>row.Id);
        return table is null?[]:table.DefaultView.Cast<DataRowView>().Select(row=>lookup[Convert.ToInt64(row["Id"])]).ToList();
    }
    /// <summary>
    /// Writes visible ordered rows or all events in the current active/trash scope after choosing a
    /// destination.
    /// </summary>
    /// <param name="all">True exports the entire active/trash scope; false uses visible sorted rows.</param>
    void Export(bool all)
    {
        using var dialog=new SaveFileDialog{Filter="CSV|*.csv",FileName=all?"TagPrinter-all.csv":"TagPrinter-report.csv"};
        if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;
        HistoryStore.ExportCsv(dialog.FileName,all?store.Query(new HistoryQuery(Deleted:trash.Checked)):VisibleRows());
    }
    /// <summary>Requests a timestamped backup filename and delegates consistent SQLite copying.</summary>
    void Backup()
    {
        using var dialog=new SaveFileDialog{Filter="SQLite|*.db",FileName=$"TagPrinter-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db"};
        if(dialog.ShowDialog(FindForm())==DialogResult.OK)store.Backup(dialog.FileName);
    }
    /// <summary>Displays report-action failures as warnings in the owning form.</summary>
    /// <param name="action">Stable event code or UI callback as specified by the containing method.</param>
    void Run(Action action){try{action();}catch(Exception ex){MessageBox.Show(FindForm(),ex.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    /// <summary>Purges all recycled rows after UI confirmation; tests call this with isolated data only.</summary>
    internal int PurgeRecycleBin()
    {
        int removed=store.EmptyRecycleBin();onTrashEmptied?.Invoke();RefreshData();return removed;
    }
    /// <summary>Finds displayed ID and exercises normal row loading without printer work.</summary>
    /// <param name="id">Stable numeric event ID, independent of serial text and display order.</param>
    internal void OpenRecordForTest(long id){int index=Grid.Rows.Cast<DataGridViewRow>().First(row=>Convert.ToInt64(row.Cells["Id"].Value)==id).Index;OpenRow(index);}
    /// <summary>Disposes the owned table before base disposal of child controls.</summary>
    /// <param name="disposing">True for explicit managed disposal; passed to the base class.</param>
    protected override void Dispose(bool disposing){if(disposing)table?.Dispose();base.Dispose(disposing);}
}
