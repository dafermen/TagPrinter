using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

/// <summary>Scale/cancellation/query-plan checks use synthetic databases, never the user's inventory or a printer.</summary>
internal static class PerformanceTests
{
    private sealed class Progress(Action<long> report):IProgress<long>{public void Report(long value)=>report(value);}
    /// <summary>Measures the same queries before/after indexes, validates bounded pages/exports and exercises real asynchronous controls.</summary>
    internal static void Run(string folder)
    {
        Ui.Language="en";string root=Path.Combine(folder,"performance-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var small=new HistoryStore(Path.Combine(root,"small.db"));Seed(small,300);VerifySemantics(small,root);VerifyBackground(small,folder);VerifyExportDialogs();VerifyInterrupt(small);
        const int rows=100000;var history=new HistoryStore(Path.Combine(root,"scale.db"));DropIndexes(history);var timer=Stopwatch.StartNew();Seed(history,rows);long seedMs=timer.ElapsedMilliseconds;var assets=new AssetStore(history);
        double before=Measure(()=>assets.Query(limit:50));timer.Restart();using(var c=history.Open())DatabasePerformance.Initialize(c);long indexesMs=timer.ElapsedMilliseconds;double after=Measure(()=>assets.Query(limit:50));
        using(var c=history.Open())
        {
            string plan=Plan(c,"SELECT record_json FROM assets ORDER BY json_extract(record_json,'$.UpdatedUtc') DESC,id DESC LIMIT 50");if(!plan.Contains("ix_perf_assets_updated") || plan.Contains("TEMP B-TREE"))throw new Exception("Default asset page lost indexed ordering: "+plan);
            plan=Plan(c,"SELECT id FROM events WHERE "+DatabasePerformance.InventoryEvent+" AND trim(serial)='S00054321' COLLATE NOCASE AND id NOT IN(SELECT event_id FROM trash)");if(!plan.Contains("SEARCH") || !plan.Contains("serial"))throw new Exception("Exact history serial query scans instead of searching an index: "+plan);
            plan=Plan(c,"SELECT uid FROM assets WHERE serial_key!='' AND serial_key='S00054321'");if(!plan.Contains("ix_asset_serial"))throw new Exception("Exact canonical serial lookup lost its unique index");
        }
        var projected=history.ReadPage(new(),0,50,"Id",true);if(projected.Rows.Count!=50 || projected.Total!=95000)throw new Exception("Scale report page/count incorrect");
        var exact=assets.ReadPage(new(Search:" s00054321 ",ExactSerial:true),0,50,"Updated",true);if(exact.Total!=1 || exact.Rows.Single().Serial!="S00054321")throw new Exception("Scale exact serial lookup failed");
        timer.Restart();var group=assets.ReadDashboard(new(ExcludeRetired:true),"Description");long groupMs=timer.ElapsedMilliseconds;if(group.TotalGroups!=80000 || group.Groups.Count!=50 || group.States.Values.Sum()!=80000)throw new Exception("Large Dashboard materialized all groups or lost totals");
        var audit=assets.ReadMovements(new string('0',31)+"1",0);if(audit.Total!=rows || audit.Rows.Count!=50)throw new Exception("Audit loaded full history");
        if(new AssetCatalog(history).Suggest("Model","M00").Count>200)throw new Exception("Large catalog suggestions were unbounded");
        string csv=Path.Combine(root,"history.csv");File.WriteAllText(csv,"previous destination");using(var cancel=new CancellationTokenSource())
        {
            RejectCanceled(()=>history.ExportAllCsv(csv,new(),cancel.Token,new Progress(n=>{if(n>=1000)cancel.Cancel();})));if(File.ReadAllText(csv)!="previous destination" || Directory.GetFiles(root,".TagPrinter-export-*.tmp").Length>0)throw new Exception("Canceled export replaced destination or leaked a temporary file");
        }
        long baseline=GC.GetTotalMemory(true),peak=baseline;var oldCulture=CultureInfo.CurrentCulture;long exportMs;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("es-CO");timer.Restart();long exported=history.ExportAllCsv(csv,new(),progress:new Progress(n=>{if(n%10000==0)peak=Math.Max(peak,GC.GetTotalMemory(true));}));exportMs=timer.ElapsedMilliseconds;if(exported!=95000 || peak-baseline>64L*1024*1024)throw new Exception("Large history export retained complete snapshots in memory");
        }
        finally{CultureInfo.CurrentCulture=oldCulture;}
        using(var parser=new Microsoft.VisualBasic.FileIO.TextFieldParser(csv,System.Text.Encoding.UTF8))
        {
            parser.SetDelimiters(";");parser.HasFieldsEnclosedInQuotes=true;parser.ReadFields();var fields=parser.ReadFields()!;if(fields[12]!="100.5" || JsonSerializer.Deserialize<WorkState>(fields[16])!.Serial!=fields[3])throw new Exception("Streaming export changed invariant numbers or raw saved state");
        }
        string masters=Path.Combine(root,"assets.csv");if(assets.ExportCsv(masters,new(Search:"S00054321",ExactSerial:true))!=1)throw new Exception("Filtered master stream ignored exact serial");
        var import=AssetCsv.Read(masters,assets);if(import.Count!=1 || import[0].Before?.Serial!="S00054321")throw new Exception("Lazy catalog import changed master export round trip");
        var report=new{Assets=rows,HistoryEvents=rows,Movements=rows,CatalogOptions=rows,SeedMilliseconds=seedMs,IndexBuildMilliseconds=indexesMs,AssetPageBeforeMilliseconds=before,AssetPageAfterMilliseconds=after,DashboardMilliseconds=groupMs,DashboardGroups=group.TotalGroups,MaterializedGroups=group.Groups.Count,ExportRows=95000,ExportMilliseconds=exportMs,ExportBytes=new FileInfo(csv).Length,AdditionalLiveManagedBytes=Math.Max(0,peak-baseline)};
        File.WriteAllText(Path.Combine(folder,"performance-measurements.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(folder,"performance-results.txt"),"PASS: 100000 assets/events/catalog options; indexed default/serial plans; bounded report/Dashboard/catalog pages; raw/invariant streaming CSV; cancellation preserves targets; bounded live memory; coalesced latest-only UI reads; native SQLite interruption; existing projection/filter/trash semantics. No printer jobs. Measurements are machine-specific, not an SLA.");
    }
    /// <summary>Creates realistic JSON snapshots in one fixture transaction; no user paths or application print hooks are involved.</summary>
    private static void Seed(HistoryStore history,int rows)
    {
        using var c=history.Open();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="CREATE TEMP TABLE sequence(i INTEGER PRIMARY KEY);WITH RECURSIVE n(i) AS(SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<$n) INSERT INTO sequence SELECT i FROM n";cmd.Parameters.AddWithValue("$n",rows);cmd.ExecuteNonQuery();cmd.Parameters.Clear();
        string date="2026-11-01T00:00:00.0000000+00:00";string asset=JsonSerializer.Serialize(new AssetRecord{Revision=1,CreatedUtc=date,UpdatedUtc=date,Brand="Bench"});string state=JsonSerializer.Serialize(new WorkState{Brand="Bench",Printer="Synthetic queue",ShowCode=false,Width=100.5m,Copies=2});
        cmd.Parameters.AddWithValue("$asset",asset);cmd.Parameters.AddWithValue("$state",state);cmd.Parameters.AddWithValue("$date",date);
        cmd.CommandText="""
            INSERT INTO assets(id,uid,serial_key,revision,record_json)
            SELECT i,printf('%032x',i),printf('S%08d',i),1,json_set($asset,'$.Uid',printf('%032x',i),'$.Number',i,'$.AssetType','Monitor','$.Model',printf('M%06d',i),'$.Serial',printf('S%08d',i),'$.Location',printf('Room %03d',i%100),'$.Custodian',printf('Team %03d',i%50),'$.Status',CASE i%5 WHEN 0 THEN 'Retired' WHEN 1 THEN 'Assigned' WHEN 2 THEN 'Repair' WHEN 3 THEN 'Available' ELSE 'Unspecified' END,'$.UpdatedUtc',printf('2026-11-%02dT%02d:%02d:%02d.0000000+00:00',1+i/86400,(i/3600)%24,(i/60)%60,i%60)) FROM sequence;
            INSERT INTO asset_catalog(kind,value_key,name) SELECT 'Model',printf('M%06d',i),printf('M%06d',i) FROM sequence;
            INSERT INTO events(id,created_utc,action,serial,asset_type,brand,model,part_number,mac_address,state_json,output_path,asset_uid)
            SELECT i,$date,CASE WHEN i%2=0 THEN 'ExportTag' ELSE 'PrintSubmitted' END,printf('S%08d',i),'Monitor','Bench',printf('M%06d',i),'','',json_set($state,'$.Serial',printf('S%08d',i),'$.AssetType','Monitor','$.Model',printf('M%06d',i),'$.FreeTextMode',CASE WHEN i%100=0 THEN json('true') ELSE json('false') END),'',printf('%032x',i) FROM sequence;
            INSERT INTO trash(event_id,deleted_utc) SELECT i,$date FROM sequence WHERE i%20=0;
            INSERT INTO asset_movements(asset_uid,at_utc,action,after_json,actor,note) SELECT printf('%032x',1),$date,'Updated',(SELECT record_json FROM assets WHERE id=1),'Synthetic','Scale audit' FROM sequence;
            DROP TABLE sequence;
            """;cmd.ExecuteNonQuery();tx.Commit();
    }
    /// <summary>Compares lightweight projection/order with original snapshots and validates paged groups and export replacement failures.</summary>
    private static void VerifySemantics(HistoryStore history,string root)
    {
        var filters=new[]{new HistoryQuery(),new HistoryQuery(Search:"S00000021",ExactSerial:true),new HistoryQuery(Deleted:true),new HistoryQuery(Search:"M0000",Field:"model"),new HistoryQuery(Action:"PrintSubmitted")};
        foreach(var filter in filters)
        {
            var page=history.ReadPage(filter,0,50,"Serial",false);var snapshots=history.Query(filter,50,sort:"Serial",descending:false);if(page.Total!=history.Count(filter) || !page.Rows.Select(r=>r.Id).SequenceEqual(snapshots.Select(r=>r.Id)))throw new Exception("Projected report changed filtering/sorting");
            for(int i=0;i<page.Rows.Count;i++)if(page.Rows[i].Copies!=snapshots[i].State.Copies || page.Rows[i].Serial!=snapshots[i].State.Serial)throw new Exception("Projected report changed display values");
        }
        var assets=new AssetStore(history);var first=assets.ReadDashboard(new(ExcludeRetired:true),"Description");var second=assets.ReadDashboard(new(ExcludeRetired:true),"Description",1);if(first.Groups.Count!=50 || second.Groups.Count!=50 || first.Groups.Select(g=>g.Model).Intersect(second.Groups.Select(g=>g.Model)).Any())throw new Exception("Aggregate pages overlap");
        string bad=Path.Combine(root,"preserve.csv");File.WriteAllText(bad,"old");IEnumerable<IEnumerable<string>> Broken(){yield return new[]{"first"};throw new IOException("Synthetic write source failure");}
        try{CsvFile.Write(bad,["Header"],Broken());throw new Exception("Expected source failure");}catch(IOException){}if(File.ReadAllText(bad)!="old")throw new Exception("Failed stream replaced prior CSV");
    }
    /// <summary>Ensures one worker, latest-only delivery, disposal safety and real control binding on the correct UI thread.</summary>
    private static void VerifyBackground(HistoryStore history,string folder)
    {
        int workers=0,applied=0;using(var queue=new BackgroundRead<int>{ForceAsync=true})
        {
            queue.Submit(token=>{Interlocked.Increment(ref workers);Thread.Sleep(120);return 1;},value=>applied=value,error=>throw error,_=>{});
            queue.Submit(token=>{Interlocked.Increment(ref workers);return 2;},value=>applied=value,error=>throw error,_=>{});queue.Submit(token=>{Interlocked.Increment(ref workers);return 3;},value=>applied=value,error=>throw error,_=>{});Pump(()=>!queue.IsBusy);if(applied!=3 || workers!=2)throw new Exception("Background reads did not coalesce or applied a stale result");
        }
        var disposed=new BackgroundRead<int>{ForceAsync=true};disposed.Submit(_=>{Thread.Sleep(80);return 4;},_=>applied=99,_=>applied=99,_=>{});disposed.Dispose();Pump(()=>!disposed.IsBusy);if(applied==99)throw new Exception("Disposed view received a callback");
        bool check=Control.CheckForIllegalCrossThreadCalls;Control.CheckForIllegalCrossThreadCalls=true;
        try
        {
            using var host=new AppWindow{Text="TagPrinter | v0.27",Width=1100,Height=700};using var report=new ReportsView(history,_=>{}){ForceAsyncReads=true};host.Controls.Add(report);host.Show();report.RefreshData();Pump(()=>report.Grid.Enabled && report.Grid.Rows.Count==50);Save(host,folder,"large-history-en.png");Ui.Language="es";report.RefreshLanguage();Pump(()=>report.Grid.Enabled);Save(host,folder,"large-history-es.png");Ui.Language="en";
            using var dashboard=new DashboardView(history,(_,_,_)=>{}){ForceAsyncReads=true};host.Controls.Clear();host.Controls.Add(dashboard);dashboard.RefreshData();Pump(()=>Descendants(dashboard).OfType<DataGridView>().Single().Enabled && dashboard.GroupRows.Count==50);Save(host,folder,"large-dashboard-en.png");dashboard.GoToGroupPage(1);Pump(()=>Descendants(dashboard).OfType<DataGridView>().Single().Enabled && dashboard.GroupRows.Count==50);Ui.Language="es";dashboard.RefreshLanguage();Pump(()=>Descendants(dashboard).OfType<DataGridView>().Single().Enabled);Save(host,folder,"large-dashboard-es.png");Ui.Language="en";host.Close();
        }
        finally{Control.CheckForIllegalCrossThreadCalls=check;}
    }
    /// <summary>Runs the actual modal worker/progress lifecycle for success, cancellation, and a failed export.</summary>
    private static void VerifyExportDialogs()
    {
        using(var success=new ExportProgressDialog((progress,token)=>{progress.Report(7);return 7;}))if(success.ShowDialog()!=DialogResult.OK || success.Written!=7 || success.Failure is not null)throw new Exception("CSV progress dialog lost successful completion");
        using(var canceled=new ExportProgressDialog((progress,token)=>{while(!token.IsCancellationRequested)Thread.Sleep(5);token.ThrowIfCancellationRequested();return 0;}))
        using(var timer=new System.Windows.Forms.Timer{Interval=40})
        {
            timer.Tick+=(_,_)=>{timer.Stop();canceled.CancelExport();};timer.Start();if(canceled.ShowDialog()!=DialogResult.Cancel || canceled.Failure is not null)throw new Exception("CSV progress dialog failed cancellation");
        }
        using(var failure=new ExportProgressDialog((progress,token)=>throw new IOException("Synthetic export failure")))if(failure.ShowDialog()!=DialogResult.Abort || failure.Failure is not IOException)throw new Exception("CSV progress dialog hid a worker failure");
    }
    /// <summary>Captures real off-screen controls after pending paints/layout settle.</summary>
    private static void Save(Form form,string folder,string name){FluentIcons.ApplyTree(form);Application.DoEvents();using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(folder,name));}
    /// <summary>Finds nested grid readiness without relying on screen coordinates.</summary>
    private static IEnumerable<Control> Descendants(Control root){yield return root;foreach(Control child in root.Controls)foreach(var nested in Descendants(child))yield return nested;}
    /// <summary>Interrupts actual running native SQL and requires cancellation to surface before an enormous join can finish.</summary>
    private static void VerifyInterrupt(HistoryStore history)
    {
        using var cancel=new CancellationTokenSource();using var started=new ManualResetEventSlim();var task=Task.Run(()=>DatabaseRead.Run(history,cancel.Token,scope=>{using var cmd=scope.Command();cmd.CommandText="WITH RECURSIVE n(i) AS(SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<100000) SELECT SUM(a.i+b.i) FROM n a CROSS JOIN n b";started.Set();return cmd.ExecuteScalar();}));
        if(!started.Wait(5000))throw new Exception("Native cancellation fixture did not start");Thread.Sleep(50);cancel.Cancel();Pump(()=>task.IsCompleted);try{task.GetAwaiter().GetResult();throw new Exception("Native SQL ignored cancellation");}catch(OperationCanceledException){}
    }
    /// <summary>Bounded nested UI pump for verification; expiry fails rather than deadlocking.</summary>
    private static void Pump(Func<bool> completed){var timer=Stopwatch.StartNew();while(!completed() && timer.ElapsedMilliseconds<10000){Application.DoEvents();Thread.Sleep(5);}if(!completed())throw new Exception("Timed out waiting for asynchronous performance verification");}
    /// <summary>Captures planner evidence rather than assuming an index name means a query uses it.</summary>
    private static string Plan(SqliteConnection c,string sql){using var cmd=c.CreateCommand();cmd.CommandText="EXPLAIN QUERY PLAN "+sql;using var reader=cmd.ExecuteReader();var parts=new List<string>();while(reader.Read())parts.Add(reader.GetString(3));return string.Join("; ",parts);}
    /// <summary>Uses median wall-clock time to reduce first-read noise; timings are evidence, not pass/fail thresholds.</summary>
    private static double Measure(Action query){var values=new List<double>();for(int i=0;i<3;i++){var timer=Stopwatch.StartNew();query();values.Add(timer.Elapsed.TotalMilliseconds);}values.Sort();return values[1];}
    /// <summary>Removes only derived indexes inside the freshly created synthetic fixture for a real before/after comparison.</summary>
    private static void DropIndexes(HistoryStore history){using var c=history.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'ix_perf_%'";var names=new List<string>();using(var reader=cmd.ExecuteReader())while(reader.Read())names.Add(reader.GetString(0));foreach(string name in names){cmd.CommandText="DROP INDEX \""+name.Replace("\"","\"\"")+"\"";cmd.ExecuteNonQuery();}}
    /// <summary>Requires cooperative/native cancellation instead of a partial success.</summary>
    private static void RejectCanceled(Action action){try{action();}catch(OperationCanceledException){return;}throw new Exception("Expected cancellation");}
}
