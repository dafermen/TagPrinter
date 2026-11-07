using System.Data;
using System.Globalization;
namespace TagPrinter;

/// <summary>Local scanner workspace. A bounded FIFO serializes durable readings; failed writes pause without losing queued input.</summary>
internal sealed class PhysicalCountsView : UserControl
{
    private readonly HistoryStore history;
    private readonly CountStore store;
    private readonly Action<string> openAsset;
    private readonly BackgroundRead<DatabasePage<CountSession>> sessionReads=new();
    private readonly BackgroundRead<CountPage> resultReads=new();
    private readonly Queue<(long Session,string Mode,string Input)> queue=new();
    private readonly DataGridView sessions=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,BackgroundColor=Color.White};
    internal readonly DataGridView ResultsGrid=new(){Name="CountResults",Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,BackgroundColor=Color.White};
    private readonly TextBox sessionSearch=new(){Width=195,MaxLength=160},search=new(){Width=160,MaxLength=250};
    private readonly ComboBox sessionState=new(){Width=130,DropDownStyle=ComboBoxStyle.DropDownList,Tag="UserData"},outcome=new(){Width=185,DropDownStyle=ComboBoxStyle.DropDownList,Tag="UserData"},mode=new(){Width=130,DropDownStyle=ComboBoxStyle.DropDownList,Tag="UserData"};
    private readonly TextBox scanner=new(){Name="CountScanner",Width=205,MaxLength=1024};
    private readonly Label header=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Padding=new Padding(0,5,0,0)},feedback=new(){Name="CountFeedback",Dock=DockStyle.Fill,AutoEllipsis=true,Padding=new Padding(0,4,0,0)},summary=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Padding=new Padding(0,7,0,0)};
    private readonly Button sessionPrevious=new(){Text="◀",Width=35},sessionNext=new(){Text="▶",Width=35},previous=new(){Text="◀",Width=35},next=new(){Text="▶",Width=35};
    private readonly Label sessionPageLabel=new(){AutoSize=true,Padding=new Padding(2,7,2,0)},pageLabel=new(){AutoSize=true,Padding=new Padding(2,7,2,0)};
    private readonly List<(Button Button,string Es,string En)> captions=[];
    private readonly Button create,scan,close,cancel,undo,asset,readings,export,retry,discard;
    private CountSession? current;
    private CountScan? lastScan;
    private string lastInput="";
    private int displayedRevision;
    private bool announcedWork;
    internal event Action<bool>? PendingWorkChanged;
    internal bool ResultsReady=>!resultBusy && current is not null && displayedRevision==current.Revision;
    private DataTable? sessionTable,resultTable;
    private int sessionPage,resultPage;
    private bool translating,binding,draining,paused,writing,sessionBusy,resultBusy,pending,disposed;
    private sealed record Choice(string Key,string Caption){public override string ToString()=>Caption;}
    internal bool ForceAsyncReads {get=>resultReads.ForceAsync;set=>resultReads.ForceAsync=sessionReads.ForceAsync=value;}
    internal bool HasPendingWork=>queue.Count>0 || draining || writing;
    internal int Queued=>queue.Count;
    internal bool QueuePaused=>paused;
    internal CountSession? Current=>current;
    internal string FeedbackText=>feedback.Text;
    /// <summary>Builds paged sessions/results and a keyboard-wedge input. Enter and the Scan button use the same enqueue path.</summary>
    internal PhysicalCountsView(HistoryStore history,Action<string> openAsset)
    {
        this.history=history;store=new(history);this.openAsset=openAsset;Dock=DockStyle.Fill;Padding=new Padding(10);BackColor=Color.White;
        var split=new SplitContainer{Size=new Size(1100,620),Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,Panel1MinSize=210,Panel2MinSize=600,SplitterDistance=245};Controls.Add(split);
        Button Add(FlowLayoutPanel bar,string es,string en,Action action){var b=new Button{AutoSize=true};captions.Add((b,es,en));b.Click+=(_,_)=>Run(action);bar.Controls.Add(b);return b;}
        var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};left.ColumnStyles.Add(new(SizeType.Percent,100));left.RowStyles.Add(new(SizeType.Absolute,145));left.RowStyles.Add(new(SizeType.Percent,100));left.RowStyles.Add(new(SizeType.Absolute,50));var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        create=Add(tools,"Nueva toma","New count",NewCount);tools.Controls.AddRange([sessionSearch,sessionState]);Add(tools,"Buscar / actualizar","Search / refresh",()=>{sessionPage=0;RefreshSessions(true);});var sessionPaging=new FlowLayoutPanel{Dock=DockStyle.Fill};sessionPaging.Controls.AddRange([sessionPrevious,sessionPageLabel,sessionNext]);left.Controls.Add(tools,0,0);left.Controls.Add(sessions,0,1);left.Controls.Add(sessionPaging,0,2);split.Panel1.Controls.Add(left);
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(10,0,0,0)};right.ColumnStyles.Add(new(SizeType.Percent,100));foreach(int height in new[]{62,44,44,44})right.RowStyles.Add(new(SizeType.Absolute,height));right.RowStyles.Add(new(SizeType.Percent,100));right.RowStyles.Add(new(SizeType.Absolute,78));right.RowStyles.Add(new(SizeType.Absolute,128));
        var input=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};input.Controls.AddRange([mode,scanner]);scan=Add(input,"Registrar","Record",()=>Enqueue(mode.SelectedItem is Choice m?m.Key:"Serial",scanner.Text));var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};filters.Controls.AddRange([outcome,search]);Add(filters,"Actualizar resultados","Refresh results",()=>{resultPage=0;RefreshResults();});
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};undo=Add(actions,"Retirar lectura","Undo reading",()=>_ = UndoSelectedAsync());asset=Add(actions,"Ver activo","View asset",OpenAsset);readings=Add(actions,"Lecturas","Readings",ShowActions);export=Add(actions,"Exportar CSV…","Export CSV…",ExportResults);close=Add(actions,"Cerrar toma","Close count",()=>_ = FinishAsync(false));cancel=Add(actions,"Cancelar toma","Cancel count",()=>_ = FinishAsync(true));retry=Add(actions,"Reintentar cola","Retry queue",RetryQueue);discard=Add(actions,"Descartar cola","Discard queue",DiscardQueue);actions.Controls.AddRange([previous,pageLabel,next]);
        right.Controls.Add(header,0,0);right.Controls.Add(input,0,1);right.Controls.Add(feedback,0,2);right.Controls.Add(filters,0,3);right.Controls.Add(ResultsGrid,0,4);right.Controls.Add(summary,0,5);right.Controls.Add(actions,0,6);split.Panel2.Controls.Add(right);
        scanner.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Enqueue(mode.SelectedItem is Choice m?m.Key:"Serial",scanner.Text);}};sessionSearch.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;sessionPage=0;RefreshSessions(true);}};search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;resultPage=0;RefreshResults();}};
        sessionState.SelectedIndexChanged+=(_,_)=>{if(!translating){sessionPage=0;RefreshSessions(true);}};outcome.SelectedIndexChanged+=(_,_)=>{if(!translating){resultPage=0;RefreshResults();}};
        ResultsGrid.SelectionChanged+=(_,_)=>UpdateEnabled();
        sessions.SelectionChanged+=(_,_)=>{if(binding || HasPendingWork || sessions.CurrentRow is null)return;long id=Convert.ToInt64(sessions.CurrentRow.Cells["Id"].Value);if(current?.Id!=id)OpenSession(id);};
        sessionPrevious.Click+=(_,_)=>{if(sessionPage>0){sessionPage--;RefreshSessions();}};sessionNext.Click+=(_,_)=>{sessionPage++;RefreshSessions();};previous.Click+=(_,_)=>{if(resultPage>0){resultPage--;RefreshResults();}};next.Click+=(_,_)=>{resultPage++;RefreshResults();};HandleCreated+=(_,_)=>{if(pending && Visible)RefreshData();};VisibleChanged+=(_,_)=>{if(Visible)RefreshData();};RefreshLanguage();
    }
    /// <summary>Refreshes captions/choice labels without modifying queue payloads or persisted count evidence.</summary>
    internal void RefreshLanguage()
    {
        translating=true;void Choices(ComboBox combo,IEnumerable<string> keys,string fallback){string key=(combo.SelectedItem as Choice)?.Key??fallback;combo.Items.Clear();foreach(string item in keys)combo.Items.Add(new Choice(item,item==""?Ui.T("Todos los estados","All statuses"):CountStore.Caption(item)));combo.SelectedIndex=Math.Max(0,combo.Items.Cast<Choice>().ToList().FindIndex(c=>c.Key==key));}
        Choices(sessionState,["","Open","Closed","Canceled"],"");Choices(outcome,["All","Pending","Missing","Found","WrongLocation","Unexpected","Retired","Unknown","Withdrawn"],"All");Choices(mode,["Serial","AssetCode"],"Serial");foreach(var c in captions)c.Button.Text=Ui.T(c.Es,c.En);sessionSearch.PlaceholderText=Ui.T("Nombre / ubicación…","Name / location…");search.PlaceholderText=Ui.T("Buscar resultado…","Search results…");scanner.PlaceholderText=Ui.T("Lee o escribe y pulsa Enter","Scan or type, then Enter");translating=false;FluentIcons.ApplyTree(this);SetHeader();if(lastScan is not null && !paused)ShowFeedback();RefreshData();UpdateEnabled();
    }
    /// <summary>Defers hidden-page work. Returning to this tab refreshes persisted counts, without resetting the active session.</summary>
    internal void RefreshData(){if(!IsHandleCreated || !Visible){pending=true;return;}pending=false;RefreshSessions();RefreshResults();}
    /// <summary>Reads a 25-session page. Binding does not silently switch an active session after a scan.</summary>
    private void RefreshSessions(bool chooseFirst=false)
    {
        if(!IsHandleCreated || !Visible){pending=true;return;}if(chooseFirst && !HasPendingWork){current=null;SetHeader();ResultsGrid.DataSource=null;}string text=sessionSearch.Text,state=(sessionState.SelectedItem as Choice)?.Key??"";int page=sessionPage;
        sessionReads.Submit(token=>store.Sessions(text,state,page,25,token),result=>
        {
            binding=true;try{sessionPage=result.Page;var data=new DataTable();data.Columns.Add("Id",typeof(long));data.Columns.Add("Name");data.Columns.Add("Status");foreach(var s in result.Rows)data.Rows.Add(s.Id,s.Code+" · "+s.Name,CountStore.Caption(s.Status));var old=sessionTable;sessionTable=data;sessions.DataSource=data;old?.Dispose();sessions.Columns["Id"].Visible=false;sessions.Columns["Name"].HeaderText=Ui.T("Tomas físicas","Physical counts");sessions.Columns["Name"].Width=160;sessions.Columns["Status"].HeaderText=Ui.T("Estado","State");sessions.Columns["Status"].Width=75;sessions.ClearSelection();sessions.CurrentCell=null;var selected=sessions.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Convert.ToInt64(r.Cells["Id"].Value)==current?.Id);if(selected is not null){sessions.CurrentCell=selected.Cells["Name"];selected.Selected=true;}sessionPageLabel.Text=$"{sessionPage+1}/{Math.Max(1,(result.Total+24)/25)} · {result.Total}";sessionPrevious.Enabled=sessionPage>0;sessionNext.Enabled=(sessionPage+1)*25<result.Total;}finally{binding=false;}if(current is null && result.Rows.Count>0)OpenSession(result.Rows[0].Id);UpdateEnabled();
        },failure=>{sessions.DataSource=null;feedback.Text=failure.Message;},busy=>{sessionBusy=busy;UpdateEnabled();});
    }
    /// <summary>Loads an explicit session and clears stale input/filter values. No reading is created by selection.</summary>
    internal void OpenSession(long id)
    {
        if(HasPendingWork)return;current=store.Get(id)??throw new InvalidDataException("Count session not found.");binding=true;try{sessions.ClearSelection();var selected=sessions.Rows.Cast<DataGridViewRow>().FirstOrDefault(row=>Convert.ToInt64(row.Cells["Id"].Value)==id);sessions.CurrentCell=selected?.Cells["Name"];if(selected is not null)selected.Selected=true;}finally{binding=false;}scanner.Clear();lastScan=null;feedback.Text="";translating=true;outcome.SelectedIndex=0;search.Clear();translating=false;resultPage=0;SetHeader();RefreshResults();UpdateEnabled();
    }
    /// <summary>Clears restored-away session references after full database replacement.</summary>
    internal void Reset(){if(HasPendingWork)throw new InvalidOperationException("Wait for queued readings.");current=null;lastScan=null;ResultsGrid.DataSource=null;SetHeader();RefreshData();}
    /// <summary>Reads bounded results and full-session reconciliation totals using the same consistent snapshot.</summary>
    internal void RefreshResults()
    {
        if(!IsHandleCreated || !Visible){pending=true;return;}if(current is null){summary.Text="";UpdateEnabled();return;}long id=current.Id;string result=(outcome.SelectedItem as Choice)?.Key??"All",text=search.Text;int page=resultPage;
        resultReads.Submit(token=>store.Results(id,result,text,page,50,token),data=>
        {
            if(current?.Id!=data.Session.Id)return;if(data.Session.Revision<current.Revision){RefreshResults();return;}current=data.Session;displayedRevision=current.Revision;resultPage=data.Page;SetHeader();var table=new DataTable();table.Columns.Add("Id",typeof(long));foreach(string key in new[]{"Outcome","AssetCode","Serial","RecordedLocation","ObservedLocation","Description","Expected","Observed","Input","ObservedAt"})table.Columns.Add(key);
            foreach(var row in data.Rows){var e=row.Entry;var a=e.Snapshot;table.Rows.Add(e.Id,CountStore.Caption(row.Outcome),a?.Code??"",a?.Serial??(e.InputValue.Length>0?e.InputValue:e.LookupKey),a?.Location??"",e.Observed?data.Session.Location:"",a is null?"":a.AssetType+" · "+a.Brand+" · "+a.Model,e.Expected?"✓":"",e.Observed?"✓":"",e.InputValue,e.ObservedUtc.Length==0?"":DateTimeOffset.Parse(e.ObservedUtc,CultureInfo.InvariantCulture).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));}
            var old=resultTable;resultTable=table;ResultsGrid.DataSource=table;old?.Dispose();ResultsGrid.Columns["Id"].Visible=false;ResultsGrid.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.True;ResultsGrid.ColumnHeadersHeight=44;string[] names=["Id",Ui.T("Resultado","Result"),Ui.T("Activo","Asset"),Ui.T("Serial / lectura","Serial / reading"),Ui.T("Ubicación registrada","Recorded location"),Ui.T("Ubicación observada","Observed location"),Ui.T("Tipo / marca / modelo","Type / brand / model"),Ui.T("Esperado","Expected"),Ui.T("Leído","Read"),Ui.T("Lectura original","Original input"),Ui.T("Fecha local","Local date")];for(int i=0;i<names.Length;i++){ResultsGrid.Columns[i].HeaderText=names[i];ResultsGrid.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;ResultsGrid.Columns[i].Width=i==6?230:i==1?135:i==2?100:i==3?110:i is 4 or 5?130:140;}
            for(int i=0;i<data.Rows.Count;i++)if(data.Rows[i].Outcome is "Missing" or "WrongLocation" or "Unknown" or "Retired")ResultsGrid.Rows[i].DefaultCellStyle.ForeColor=Color.Firebrick;
            var t=data.Totals;summary.Text=string.Format(Ui.T("Esperados: {0:N0} · Encontrados: {1:N0} · {2}: {3:N0}\nUbicación incorrecta: {4:N0} · No esperados: {5:N0} · Dados de baja: {6:N0} · No reconocidos: {7:N0}\nLecturas guardadas: {8:N0} · Repetidas: {9:N0}","Expected: {0:N0} · Found: {1:N0} · {2}: {3:N0}\nWrong location: {4:N0} · Unexpected: {5:N0} · Retired: {6:N0} · Unrecognized: {7:N0}\nSaved readings: {8:N0} · Duplicates: {9:N0}"),t.Expected,t.Found,CountStore.Caption(current.Status=="Closed"?"Missing":"Pending"),t.Pending,t.WrongLocation,t.Unexpected,t.Retired,t.Unknown,t.Readings,t.Duplicates);pageLabel.Text=$"{resultPage+1}/{Math.Max(1,(data.Total+49)/50)} · {data.Total}";previous.Enabled=resultPage>0;next.Enabled=(resultPage+1)*50<data.Total;UpdateEnabled();
        },failure=>{ResultsGrid.DataSource=null;feedback.ForeColor=Color.Firebrick;feedback.Text=failure.Message;},busy=>{resultBusy=busy;ResultsGrid.Enabled=!busy;UpdateEnabled();});
    }
    /// <summary>Queues at most 100 unsaved readings, retaining overflow text instead of silently dropping it.</summary>
    internal bool Enqueue(string inputMode,string input)
    {
        try{if(current?.Status!="Open" || writing || paused)throw new InvalidOperationException(Ui.T("Selecciona una toma abierta y revisa la cola.","Select an open count and check the queue."));input=CountStore.Token(inputMode,input);if(queue.Count>=100)throw new InvalidOperationException(Ui.T("Cola llena. Espera y vuelve a registrar esta lectura.","Queue full. Wait and submit this reading again."));queue.Enqueue((current.Id,inputMode,input));scanner.Clear();SetHeader();UpdateEnabled();if(!draining)_=DrainAsync();return true;}catch(Exception error){feedback.ForeColor=Color.Firebrick;feedback.Text=error.Message;return false;}
    }
    /// <summary>Serializes commits. A failed head stays queued for explicit retry/discard; only durable success is acknowledged.</summary>
    private async Task DrainAsync()
    {
        if(draining || disposed)return;draining=true;paused=false;UpdateEnabled();
        try
        {
            while(queue.Count>0 && !disposed)
            {
                var request=queue.Peek();CountScan saved;try{saved=await Task.Run(()=>store.Scan(request.Session,request.Mode,request.Input));}catch(Exception error){if(!disposed){paused=true;feedback.ForeColor=Color.Firebrick;feedback.Text=Ui.T("Lecturas sin guardar: ","Unsaved readings: ")+queue.Count+" · "+request.Input+" · "+Ui.T("Reintenta o descarta la cola: ","Retry or discard the queue: ")+error.Message;}break;}
                if(disposed)break;queue.Dequeue();if(current?.Id==saved.Session.Id){current=saved.Session;lastScan=saved;lastInput=request.Input;ShowFeedback();SetHeader();RefreshResults();}
            }
        }
        finally{draining=false;if(!disposed){UpdateEnabled();RefreshSessions();if(queue.Count==0)RefreshResults();}}
    }
    /// <summary>Re-renders durable scan feedback when the interface language changes.</summary>
    private void ShowFeedback(){if(lastScan is not {} saved)return;feedback.ForeColor=saved.Duplicate?Color.DarkGoldenrod:saved.Result=="Found"?Color.DarkGreen:Color.Firebrick;feedback.Text=(saved.Duplicate?CountStore.Caption("Duplicate")+" · ":"")+CountStore.Caption(saved.Result)+" · "+lastInput+" · "+Ui.T("En cola: ","Queued: ")+queue.Count;}
    /// <summary>Retries retained unsaved input without asking the operator to scan it again.</summary>
    internal void RetryQueue(){if(paused && !draining && queue.Count>0){paused=false;_=DrainAsync();}}
    /// <summary>Discards only explicitly confirmed unsaved input; committed readings and audit are preserved.</summary>
    private void DiscardQueue(){if(!paused || draining)return;if(MessageBox.Show(FindForm(),string.Format(Ui.T("¿Descartar las {0} lecturas que aún no se guardaron? Las lecturas confirmadas se conservarán.","Discard the {0} readings not yet saved? Confirmed readings remain stored."),queue.Count),"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes){queue.Clear();paused=false;feedback.Text=Ui.T("Cola no guardada descartada.","Unsaved queue discarded.");RefreshData();UpdateEnabled();}}
    /// <summary>Reflects queued/paused/write state. Input stays enabled while a healthy FIFO drains.</summary>
    private void UpdateEnabled()
    {
        if(disposed)return;if(announcedWork!=HasPendingWork){announcedWork=HasPendingWork;PendingWorkChanged?.Invoke(announcedWork);}bool idle=!HasPendingWork,open=current?.Status=="Open";sessions.Enabled=idle&&!sessionBusy;create.Enabled=sessionSearch.Enabled=sessionState.Enabled=idle;scanner.Enabled=scan.Enabled=open&&!writing&&!paused;mode.Enabled=idle&&open;close.Enabled=cancel.Enabled=idle&&open&&!resultBusy;undo.Enabled=idle&&open&&!resultBusy&&ResultsGrid.CurrentRow?.Cells["Observed"].Value?.ToString()=="✓";readings.Enabled=export.Enabled=idle&&current is not null&&!resultBusy;asset.Enabled=idle&&current is not null&&!resultBusy&&ResultsGrid.CurrentRow?.Cells["AssetCode"].Value?.ToString() is {Length:>0};retry.Visible=discard.Visible=paused;retry.Enabled=discard.Enabled=paused&&!draining;sessionPrevious.Enabled&=idle;sessionNext.Enabled&=idle;previous.Enabled&=idle;next.Enabled&=idle;
    }
    /// <summary>Displays the immutable scope and current lifecycle independently of result filtering.</summary>
    private void SetHeader(){header.Text=current is null?Ui.T("Crea o selecciona una toma física.","Create or select a physical count."):current.Code+" · "+current.Name+"\n"+Ui.T("Ubicación: ","Location: ")+current.Location+" · "+CountStore.Caption(current.Status)+" · "+Ui.T("Esperados al inicio: ","Expected at start: ")+current.ExpectedCount+" · "+Ui.T("En cola: ","Queued: ")+queue.Count;}
    /// <summary>Starts a scope snapshot only after explicit form confirmation.</summary>
    private void NewCount(){if(HasPendingWork)return;using var dialog=new NewCountDialog(history);if(dialog.ShowDialog(FindForm())==DialogResult.OK){sessionPage=0;translating=true;sessionState.SelectedIndex=0;sessionSearch.Clear();translating=false;OpenSession(dialog.CreatedSession!.Id);RefreshSessions();}}
    /// <summary>Closes/cancels only after showing the consequence for remaining expected rows.</summary>
    private async Task FinishAsync(bool canceled)
    {
        if(current is null || HasPendingWork)return;string question=canceled?Ui.T("¿Cancelar esta toma? Se conservarán sus lecturas; los pendientes no se declararán faltantes.","Cancel this count? Readings are preserved; pending assets will not be declared missing."):Ui.T("¿Cerrar esta toma? Los activos esperados sin lectura quedarán como faltantes. El resultado será de solo lectura.","Close this count? Expected assets without an effective reading become missing. Results become read-only.");if(MessageBox.Show(FindForm(),question,"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;var selected=current;await MutateAsync(()=>store.Finish(selected.Id,selected.Revision,canceled));
    }
    /// <summary>Withdraws only the selected effective reading; its original action remains in Readings.</summary>
    private async Task UndoSelectedAsync(){if(current is null || HasPendingWork || ResultsGrid.CurrentRow is null)return;long entry=Convert.ToInt64(ResultsGrid.CurrentRow.Cells["Id"].Value);if(MessageBox.Show(FindForm(),Ui.T("¿Retirar esta lectura? Se conservará su historial.","Withdraw this reading? Its history will be preserved."),"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;var selected=current;await MutateAsync(()=>store.Undo(selected.Id,entry,selected.Revision));}
    /// <summary>Performs explicit state changes outside the UI thread and refreshes latest persisted revisions.</summary>
    private async Task MutateAsync(Func<CountSession> mutation){writing=true;UpdateEnabled();try{var saved=await Task.Run(mutation);if(!disposed)current=saved;}catch(Exception error){if(!disposed){feedback.ForeColor=Color.Firebrick;feedback.Text=error.Message;}}finally{writing=false;if(!disposed){SetHeader();RefreshData();UpdateEnabled();}}}
    /// <summary>Opens the current asset by stable GUID; unresolved input never creates a new master automatically.</summary>
    private void OpenAsset(){if(current is null || ResultsGrid.CurrentRow is null)return;long id=Convert.ToInt64(ResultsGrid.CurrentRow.Cells["Id"].Value);using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT asset_uid FROM count_entries WHERE id=$id AND session_id=$session";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$session",current.Id);if(cmd.ExecuteScalar() is string uid)openAsset(uid);else feedback.Text=Ui.T("La lectura no está vinculada a un activo. Revisa o registra el activo y vuelve a leerlo.","This reading has no asset link. Review/register the asset and scan it again.");}
    /// <summary>Displays a bounded immutable action history including duplicate and withdrawn attempts.</summary>
    private void ShowActions(){if(current is null)return;using var dialog=new AppWindow{Text=current.Code+" · "+Ui.T("Lecturas","Readings"),Width=1100,Height=600,StartPosition=FormStartPosition.CenterParent};dialog.Controls.Add(new CountActionsView(store,current.Id));dialog.ShowDialog(FindForm());}
    /// <summary>Exports every filtered page using an atomic destination and a consistent SQLite read snapshot.</summary>
    private void ExportResults(){if(current is null)return;using var file=new SaveFileDialog{Filter="CSV|*.csv",DefaultExt="csv",AddExtension=true,FileName=current.Code+".csv"};if(file.ShowDialog(FindForm())!=DialogResult.OK)return;long id=current.Id;string result=(outcome.SelectedItem as Choice)?.Key??"All",text=search.Text,path=file.FileName;ExportProgressDialog.Run(FindForm(),(progress,token)=>store.ExportCsv(path,id,result,text,token,progress));}
    /// <summary>Shows recoverable command errors without hiding unattended test failures.</summary>
    private void Run(Action action){try{action();}catch(Exception error){if(Environment.GetCommandLineArgs().Contains("--self-test"))throw;MessageBox.Show(FindForm(),error.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    /// <summary>Stops read callbacks. MainForm prevents normal closure while writes/input remain pending.</summary>
    /// <param name="disposing">True for explicit managed disposal.</param>
    protected override void Dispose(bool disposing){if(disposing){disposed=true;sessionReads.Dispose();resultReads.Dispose();sessionTable?.Dispose();resultTable?.Dispose();}base.Dispose(disposing);}
}
