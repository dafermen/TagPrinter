using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

/// <summary>Real SQLite, date, revision-chain, backup, CSV and bilingual UI checks using synthetic assets only.</summary>
internal static class ServiceTests
{
    /// <summary>Runs the service module independently of printers and real user inventory/settings.</summary>
    internal static void Run(string folder)
    {
        Ui.Language="en";string root=Path.Combine(folder,"services-"+Guid.NewGuid());Directory.CreateDirectory(root);var history=new HistoryStore(Path.Combine(root,"inventory.db"));var assets=new AssetStore(history);var store=new ServiceStore(history);var asset=assets.Save(new AssetRecord{AssetType="Laptop",Brand="Dell",Model="Latitude",Serial="000123",Location="HQ"});
        var maintenance=store.Save(new ServiceRecord{AssetUid=asset.Uid,Title="Preventive inspection",DueDate="2026-11-06",Cost=125.50m,Currency=" usd ",Provider="Local service",Notes="Line; \"quoted\"\nsecond line"});
        var warranty=store.Save(new ServiceRecord{Kind="Warranty",AssetUid=asset.Uid,Title="Manufacturer coverage",Category="",Status="Active",StartDate="2025-11-07",DueDate="2026-11-07",Provider="Dell",Reference="CASE-001"});
        if(history.Count()!=0 || assets.Get(asset.Uid)!.Status!="Available" || maintenance.Currency!="USD" || maintenance.Id==warranty.Id)throw new Exception("Services changed inventory events/asset state or lost explicit currency");
        var day=new DateOnly(2026,11,7);var jobs=store.ReadPage(new("Maintenance"),day:day);var warranties=store.ReadPage(new("Warranty"),day:day);if(jobs.Late!=1 || warranties.Late!=0 || warranties.Upcoming!=1 || ServiceStore.Alert(warranty,day,30)!="Expiring soon" || ServiceStore.Alert(warranty,day.AddDays(1),30)!="Expired")throw new Exception("Inclusive warranty/date alerts failed");
        ServiceStore.Validate(warranty with {StartDate="2024-02-29",DueDate="2024-02-29"});Reject<InvalidDataException>(()=>ServiceStore.Validate(warranty with {StartDate="2025-02-29"}));Reject<InvalidDataException>(()=>ServiceStore.Validate(warranty with {DueDate="2025-11-06"}));
        foreach(var invalid in new[]{maintenance with {Title=" "},maintenance with {Cost=-1},maintenance with {Cost=1.001m},maintenance with {Currency="US"},maintenance with {Cost=null,Currency="USD"},maintenance with {Status="Completed"},maintenance with {Status="Unknown"},maintenance with {AssetUid=Guid.NewGuid().ToString("N")},maintenance with {Notes=new string('x',4001)}})Reject<InvalidDataException>(()=>store.Save(invalid));
        var working=store.Save(maintenance with {Status="InProgress",StartDate="2026-11-07"});Reject<InvalidOperationException>(()=>store.Save(maintenance with {Notes="stale edit"}));if(store.Get(maintenance.Id)!=working || store.ReadChanges(maintenance.Id).Total!=2)throw new Exception("Stale save changed service/audit");
        var complete=store.Save(working with {Status="Completed",CompletedDate="2026-11-07",Notes="Inspected and cleaned"});if(store.Save(complete)!=complete || store.ReadChanges(complete.Id).Total!=3 || store.ReadPage(new("Maintenance",Deadline:"Attention"),day:day).Total!=0)throw new Exception("Completed jobs still alert or unchanged save created an audit revision");
        Reject<InvalidOperationException>(()=>store.Save(complete with {AssetUid=assets.Save(new AssetRecord{AssetType="Monitor",Serial="OTHER"}).Uid}));
        // A database failure between the business write and audit must roll back both writes.
        Sql(history.DatabasePath,"CREATE TRIGGER fail_service_audit BEFORE INSERT ON service_changes BEGIN SELECT RAISE(ABORT,'synthetic audit failure');END;");Reject<SqliteException>(()=>store.Save(complete with {Notes="must roll back"}));Sql(history.DatabasePath,"DROP TRIGGER fail_service_audit;");if(store.Get(complete.Id)!=complete || store.ReadChanges(complete.Id).Total!=3)throw new Exception("Failed service audit partially committed");
        asset=assets.Save(asset with {Serial="000456",Model="Updated model"});if(store.ReadPage(new("Warranty",Search:"000456"),day:day).Rows.Single().Record.Id!=warranty.Id || store.ReadPage(new("Warranty",Search:"000123"),day:day).Total!=0)throw new Exception("Services lost stable asset identity after serial edit");
        string csv=Path.Combine(root,"maintenance.csv");var prior=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("es-CO");if(store.ExportCsv(csv,new("Maintenance",IncludeRetired:true))!=1)throw new Exception("Service export scope mismatch");}finally{CultureInfo.CurrentCulture=prior;}
        using(var parser=new Microsoft.VisualBasic.FileIO.TextFieldParser(csv,System.Text.Encoding.UTF8)){parser.SetDelimiters(";");parser.HasFieldsEnclosedInQuotes=true;var header=parser.ReadFields()!;var row=parser.ReadFields()!;if(header.Length!=23 || row[7]!="000456" || row[17]!="125.50" || row[18]!="USD" || row[19]!="Inspected and cleaned")throw new Exception("Service CSV changed invariant costs/identity");}
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();File.WriteAllText(csv,"existing");Reject<OperationCanceledException>(()=>store.ExportCsv(csv,new("Maintenance"),cancel.Token));if(File.ReadAllText(csv)!="existing")throw new Exception("Canceled service export replaced destination");}
        TestPages(history,store,asset);TestBackups(root,history,store,warranty);TestMigration(root,history);TestUi(folder,root,history,asset);
        File.WriteAllText(Path.Combine(folder,"services-results.txt"),"PASS: linked maintenance/warranties; inclusive calendar/invalid/leap dates; explicit decimal currency; lifecycle/immutable-identity/no-op/stale guards; audit-write rollback; 10000-row bounded/indexed pages and retired scopes; paged changes; stable serial edits; invariant/canceled CSV; complete backup/restore and malformed record/audit rejection; protected schema-3 migration and old backup restore; actual editor validation/bilingual/async/minimum-size UI. Local, no accounts or printer jobs.");Ui.Language="en";
    }
    /// <summary>Seeds a large valid service registry in one isolated transaction, then verifies indexed bounded reads and audit pages.</summary>
    private static void TestPages(HistoryStore history,ServiceStore store,AssetRecord asset)
    {
        using(var c=history.Open())using(var tx=c.BeginTransaction())using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;var seed=store.Get(1)!;cmd.Parameters.AddWithValue("$uid",asset.Uid);cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(seed with {Id=0,Revision=1,Status="Planned",StartDate="",CompletedDate="",Cost=null,Currency="",Notes="Scale fixture"}));cmd.CommandText="""
                CREATE TEMP TABLE series(i INTEGER PRIMARY KEY);WITH RECURSIVE n(i) AS(SELECT 1000 UNION ALL SELECT i+1 FROM n WHERE i<10999) INSERT INTO series SELECT i FROM n;
                INSERT INTO asset_services(id,asset_uid,kind,revision,due_date,status,record_json) SELECT i,$uid,'Maintenance',1,'2026-12-01','Planned',json_set($json,'$.Id',i,'$.DueDate','2026-12-01','$.Title',printf('Scale %05d',i)) FROM series;
                INSERT INTO service_changes(service_id,at_utc,after_json) SELECT s.id,json_extract(s.record_json,'$.UpdatedUtc'),s.record_json FROM asset_services s JOIN series n ON s.id=n.i;
                DROP TABLE series;
                """;cmd.ExecuteNonQuery();tx.Commit();
        }
        var first=store.ReadPage(new("Maintenance",Search:"Scale"));var second=store.ReadPage(new("Maintenance",Search:"Scale"),1);if(first.Total!=10000 || first.Rows.Count!=50 || second.Rows.Count!=50 || first.Rows.Select(r=>r.Record.Id).Intersect(second.Rows.Select(r=>r.Record.Id)).Any())throw new Exception("Large service pages overlapped or loaded the registry");
        var item=first.Rows[0].Record;for(int i=0;i<53;i++)item=store.Save(item with {Notes="Revision "+i});if(store.ReadChanges(item.Id).Rows.Count!=50 || store.ReadChanges(item.Id,1).Rows.Count!=4)throw new Exception("Service audit is not paged");
        using(var c=history.Open())using(var cmd=c.CreateCommand()){cmd.CommandText="EXPLAIN QUERY PLAN SELECT id FROM asset_services WHERE kind='Maintenance' ORDER BY due_date,id LIMIT 50";using var reader=cmd.ExecuteReader();bool indexed=false;while(reader.Read())if(reader.GetString(3).Contains("ix_services_due"))indexed=true;if(!indexed)throw new Exception("Service page does not use due-date index");}
        var assets=new AssetStore(history);var retired=assets.Save(new AssetRecord{AssetType="Old scanner",Serial="RETIRED-SERVICE",Status="Retired"});store.Save(ServiceEditor.Draft("Warranty",retired.Uid) with {Title="Historical warranty"});if(store.ReadPage(new("Warranty",AssetUid:retired.Uid)).Total!=0 || store.ReadPage(new("Warranty",AssetUid:retired.Uid,IncludeRetired:true)).Total!=1)throw new Exception("Retired service scope failed");
    }
    /// <summary>Proves full restore covers both kinds/audits and rejects semantically corrupt SQLite without replacing live data.</summary>
    private static void TestBackups(string root,HistoryStore history,ServiceStore store,ServiceRecord warranty)
    {
        string backup=Path.Combine(root,"complete.db");history.Backup(backup);var changed=store.Save(warranty with {Status="Claimed",Reference="CLAIM-001"});history.RestoreBackup(backup,Path.Combine(root,"safety"));if(store.Get(warranty.Id)!=warranty || store.ReadChanges(warranty.Id).Total!=1)throw new Exception("SQLite backup did not restore service records/audit");
        string bad=Path.Combine(root,"bad-service.db");history.Backup(bad);Sql(bad,"UPDATE asset_services SET record_json=json_set(record_json,'$.DueDate','bad date') WHERE id="+warranty.Id);Reject<InvalidDataException>(()=>history.RestoreBackup(bad,Path.Combine(root,"rejected")));if(store.Get(warranty.Id)!=warranty)throw new Exception("Corrupt service backup replaced live data");
        history.Backup(bad);Sql(bad,"UPDATE service_changes SET after_json=json_set(after_json,'$.Reference','tampered') WHERE service_id="+warranty.Id);Reject<InvalidDataException>(()=>history.RestoreBackup(bad,Path.Combine(root,"rejected")));if(store.Get(warranty.Id)!=warranty)throw new Exception("Broken service audit backup replaced live data");
        history.Backup(bad);Sql(bad,"DELETE FROM service_changes WHERE service_id="+warranty.Id);Reject<InvalidDataException>(()=>HistoryStore.ValidateBackup(bad));
    }
    /// <summary>Constructs a genuine schema-3 fixture and checks one preserved safety copy plus safe staged legacy restoration.</summary>
    private static void TestMigration(string root,HistoryStore source)
    {
        string directory=Path.Combine(root,"legacy3");Directory.CreateDirectory(directory);string path=Path.Combine(directory,"inventory.db");source.Backup(path);Sql(path,"DROP TABLE count_actions;DROP TABLE count_entries;DROP TABLE count_sessions;DROP TABLE service_changes;DROP TABLE asset_services;PRAGMA user_version=3;");string json=Scalar(path,"SELECT record_json FROM assets ORDER BY id LIMIT 1");var upgraded=new HistoryStore(path);if(Scalar(path,"PRAGMA user_version")!="5" || Scalar(path,"SELECT record_json FROM assets ORDER BY id LIMIT 1")!=json)throw new Exception("Service migration changed asset snapshots");string safety=Directory.GetFiles(Path.Combine(directory,"backups"),"TagPrinter-before-services-*.db").Single();if(Scalar(safety,"PRAGMA user_version")!="3")throw new Exception("Service safety copy was upgraded");new HistoryStore(path);if(Directory.GetFiles(Path.Combine(directory,"backups"),"TagPrinter-before-services-*.db").Length!=1)throw new Exception("Service migration repeats on startup");upgraded.RestoreBackup(safety,Path.Combine(directory,"restore-safety"));if(Scalar(path,"PRAGMA user_version")!="5" || new ServiceStore(upgraded).ReadPage(new("Warranty")).Total!=0)throw new Exception("Legacy restore failed staged service migration");
    }
    /// <summary>Exercises the actual save bindings, validation feedback, async list and translated workspace at minimum size.</summary>
    private static void TestUi(string folder,string root,HistoryStore history,AssetRecord asset)
    {
        using(var editor=new ServiceEditor(history,ServiceEditor.Draft("Maintenance",asset.Uid),asset))
        {
            editor.Show();if(editor.TrySave() || editor.DialogResult!=DialogResult.None || editor.Controls.Find("ServiceError",true).Single().Text.Length==0)throw new Exception("Editor discarded invalid service or hid validation");editor.Controls.Find("ServiceTitle",true).Single().Text="Cooling system cleaning";((CheckBox)editor.Controls.Find("ServiceHasCost",true).Single()).Checked=true;((NumericUpDown)editor.Controls.Find("ServiceCost",true).Single()).Value=42.75m;if(editor.TrySave())throw new Exception("Editor accepted amount without currency");editor.Controls.Find("ServiceCurrency",true).Single().Text="USD";Save(editor,folder,"maintenance-editor-en.png");if(!editor.TrySave() || editor.Saved?.Cost!=42.75m)throw new Exception("Actual maintenance save binding failed");editor.Close();
        }
        Ui.Language="es";using(var editor=new ServiceEditor(history,ServiceEditor.Draft("Warranty",asset.Uid),asset)){editor.Show();editor.Controls.Find("ServiceTitle",true).Single().Text="Garantía del fabricante";Save(editor,folder,"warranty-editor-es.png");if(!editor.TrySave() || editor.Saved?.Kind!="Warranty")throw new Exception("Warranty editor failed");editor.Close();}Ui.Language="en";
        using var form=new MainForm(Path.Combine(root,"ui-settings.json"),AppConfig.CreateDefault(),history.DatabasePath);form.Show();form.ShowServices(asset.Uid);var list=form.Services.List();list.ForceAsyncReads=true;list.RefreshData();Pump(()=>list.Grid.Enabled && list.Grid.Rows.Count==50);Save(form,folder,"maintenance-en.png");form.Restore(form.CaptureWork() with {Language="es"});form.ShowServices(asset.Uid);list=form.Services.List(true);list.ForceAsyncReads=true;list.RefreshData();Pump(()=>list.Grid.Enabled && list.Grid.Rows.Count>0);Save(form,folder,"warranties-es.png");form.Size=form.MinimumSize;Save(form,folder,"services-small-es.png");if(list.Grid.Height<100 || list.Grid.Width<300 || list.Grid.GetColumnDisplayRectangle(list.Grid.Columns["Alert"].Index,false).Right>list.Grid.ClientSize.Width)throw new Exception("Service grid unusable at minimum size");form.ShowAssets(new(AssetUid:asset.Uid));if(new AssetStore(history).Count(new(AssetUid:asset.Uid))!=1)throw new Exception("View asset navigation is not exact");form.Close();Ui.Language="en";
    }
    /// <summary>Captures real controls without activating or moving windows onto the user's desktop.</summary>
    private static void Save(Form form,string folder,string name){Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,name));}
    /// <summary>Pumps async continuations with a deadline instead of hanging an unattended test.</summary>
    private static void Pump(Func<bool> done){var watch=Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<15000){Application.DoEvents();Thread.Sleep(5);}if(!done())throw new Exception("Service UI read timed out");}
    /// <summary>Mutates only the explicit isolated fixture path supplied by the calling test.</summary>
    private static void Sql(string path,string sql){using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();}
    /// <summary>Reads a fixture value without creating application UI or migrating the file.</summary>
    private static string Scalar(string path,string sql){using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToString(cmd.ExecuteScalar(),CultureInfo.InvariantCulture)!;}
    /// <summary>Requires the specified error type; unexpected success must fail verification.</summary>
    private static void Reject<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
