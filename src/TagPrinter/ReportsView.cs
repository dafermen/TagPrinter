using System.Data;
namespace TagPrinter;

public sealed class ReportsView : UserControl
{
    readonly HistoryStore store;
    readonly Action<HistoryEvent> load;
    readonly TextBox search=new(){Width=220};
    readonly ComboBox field=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=145}, action=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=165};
    readonly DateTimePicker from=new(){Format=DateTimePickerFormat.Short,ShowCheckBox=true,Checked=false,Width=125,Value=DateTime.Today.AddDays(-7)}, to=new(){Format=DateTimePickerFormat.Short,ShowCheckBox=true,Checked=false,Width=125};
    readonly Label searchLabel=new(){AutoSize=true}, fromLabel=new(){AutoSize=true}, toLabel=new(){AutoSize=true}, count=new(){Dock=DockStyle.Fill,AutoSize=false};
    readonly Button apply=new(){AutoSize=true}, reset=new(){AutoSize=true}, export=new(){AutoSize=true}, exportAll=new(){AutoSize=true}, backup=new(){AutoSize=true}, open=new(){AutoSize=true};
    internal readonly DataGridView Grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Color.White,RowHeadersVisible=false};
    List<HistoryEvent> current=[];
    DataTable? table;
    static readonly string[] Fields=["all","serial","asset_type","brand","model","part_number","mac_address"];
    public ReportsView(HistoryStore history,Action<HistoryEvent> onLoad)
    {
        store=history;load=onLoad;BackColor=Color.White;Dock=DockStyle.Fill;Padding=new Padding(16);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,88));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        filters.Controls.AddRange([searchLabel,search,field,action,fromLabel,from,toLabel,to,apply,reset]);
        layout.Controls.Add(filters,0,0);layout.Controls.Add(Grid,0,1);layout.Controls.Add(count,0,2);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};buttons.Controls.AddRange([open,export,exportAll,backup]);layout.Controls.Add(buttons,0,3);Controls.Add(layout);
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
        field.Items.AddRange(new object[Fields.Length].Select(_=>(object)"").ToArray());field.SelectedIndex=0;
        action.Items.AddRange(new object[HistoryStore.Actions.Length+1].Select(_=>(object)"").ToArray());action.SelectedIndex=0;
        from.Checked=false;to.Checked=false;RefreshLanguage();
    }
    static string ActionText(string value)=>value switch
    {
        "ExportTag"=>Ui.T("Etiqueta exportada","Tag exported"),"ExportCode"=>Ui.T("Código exportado","Code exported"),"ExportZpl"=>Ui.T("ZPL exportado","ZPL exported"),"PrintSubmitted"=>Ui.T("Enviado a impresión","Sent to printer"),_=>value
    };
    public void RefreshLanguage()
    {
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
    HistoryQuery Filter()
    {
        if(from.Checked && to.Checked && from.Value.Date>to.Value.Date)throw new ArgumentException(Ui.T("La fecha inicial debe ser anterior o igual a la final.","Start date must not be after end date."));
        DateTimeOffset? start=from.Checked?new DateTimeOffset(DateTime.SpecifyKind(from.Value.Date,DateTimeKind.Local)).ToUniversalTime():null;
        DateTimeOffset? end=to.Checked?new DateTimeOffset(DateTime.SpecifyKind(to.Value.Date.AddDays(1),DateTimeKind.Local)).ToUniversalTime():null;
        return new(search.Text.Trim(),Fields[Math.Max(0,field.SelectedIndex)],action.SelectedIndex>0?HistoryStore.Actions[action.SelectedIndex-1]:"",start,end);
    }
    public void RefreshData()
    {
        current=store.Query(Filter());string sort=table?.DefaultView.Sort??"";
        var next=new DataTable();
        next.Columns.Add("Id",typeof(long));next.Columns.Add("Date",typeof(DateTime));foreach(var name in new[]{"Action","Serial","AssetType","Brand","Model","PartNumber","MacAddress","Printer"})next.Columns.Add(name,typeof(string));next.Columns.Add("Copies",typeof(int));next.Columns.Add("CodeType",typeof(string));next.Columns.Add("SourceEventId",typeof(long));
        foreach(var row in current){var state=row.State;next.Rows.Add(row.Id,row.CreatedUtc.LocalDateTime,ActionText(row.Action),state.Serial,state.AssetType,state.Brand,state.Model,state.PartNumber,state.MacAddress,state.Printer,state.Copies,state.Code==1?"QR":"Code 128",(object?)row.SourceEventId??DBNull.Value);}
        if(sort.Length>0)next.DefaultView.Sort=sort;
        var old=table;table=next;Grid.DataSource=next;old?.Dispose();
        count.Text=string.Format(Ui.T("{0} registros. Pulsa un serial para cargar la etiqueta; no se imprime automáticamente.","{0} records. Click a serial to load its label; it will not print automatically."),current.Count);
    }
    void OpenRow(int index)
    {
        if(index<0 || index>=Grid.Rows.Count)throw new InvalidOperationException(Ui.T("Selecciona un registro.","Select a record."));
        long id=Convert.ToInt64(Grid.Rows[index].Cells["Id"].Value);load(store.Get(id)??throw new InvalidDataException("Record not found."));
    }
    internal List<HistoryEvent> VisibleRows()
    {
        var lookup=current.ToDictionary(row=>row.Id);
        return table is null?[]:table.DefaultView.Cast<DataRowView>().Select(row=>lookup[Convert.ToInt64(row["Id"])]).ToList();
    }
    void Export(bool all)
    {
        using var dialog=new SaveFileDialog{Filter="CSV|*.csv",FileName=all?"TagPrinter-all.csv":"TagPrinter-report.csv"};
        if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;
        HistoryStore.ExportCsv(dialog.FileName,all?store.Query():VisibleRows());
    }
    void Backup()
    {
        using var dialog=new SaveFileDialog{Filter="SQLite|*.db",FileName=$"TagPrinter-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db"};
        if(dialog.ShowDialog(FindForm())==DialogResult.OK)store.Backup(dialog.FileName);
    }
    void Run(Action action){try{action();}catch(Exception ex){MessageBox.Show(FindForm(),ex.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    internal void OpenRecordForTest(long id){int index=Grid.Rows.Cast<DataGridViewRow>().First(row=>Convert.ToInt64(row.Cells["Id"].Value)==id).Index;OpenRow(index);}
    protected override void Dispose(bool disposing){if(disposing)table?.Dispose();base.Dispose(disposing);}
}
