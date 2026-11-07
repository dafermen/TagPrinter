using Microsoft.Data.Sqlite;
using System.Diagnostics;
namespace TagPrinter;

/// <summary>Exercises inventory workflows with real SQLite/forms and isolated files; never submits or queries printer jobs.</summary>
internal static class AssetOperationsTests
{
    /// <summary>Checks catalogs, serial recognition, workflow reasons, reviewed imports, scoped Dashboard links and durable backup feedback.</summary>
    internal static void Run(string folder)
    {
        Ui.Language="en";string root=Path.Combine(folder,"asset-operations-"+Guid.NewGuid());Directory.CreateDirectory(root);var history=new HistoryStore(Path.Combine(root,"inventory.db"));var store=new AssetStore(history);var catalog=new AssetCatalog(history);
        if(catalog.Add("Location"," Storage ")!="Storage" || catalog.Add("Location","STORAGE")!="Storage" || catalog.Values("Location").Count!=1)throw new Exception("Catalog duplicates were not normalized");
        catalog.Add("Location","Bogotá");if(catalog.Add("Location","BOGOTÁ")!="Bogotá")throw new Exception("Unicode catalog normalization failed");
        catalog.Add("Brand","Dell");var original=store.Save(new AssetRecord{AssetType="Monitor",Brand=" dell ",Model="M",Serial="000001",Location="storage",Notes="Notes; \"quoted\"\nsecond line"});
        if(original.Brand!="Dell" || original.Location!="Storage")throw new Exception("Catalog spelling not applied to saved master");
        using(var dataCombo=new ComboBox{Tag="UserData",Text="Model"}){dataCombo.Items.Add("Model");Ui.Language="es";Ui.Translate(dataCombo);if(dataCombo.Text!="Model" || dataCombo.Items[0]?.ToString()!="Model")throw new Exception("Catalog user values were translated");Ui.Language="en";}
        using(var view=new AssetsView(history,_=>{}))
        {
            using(var catalogs=view.CreateCatalogEditor())
            {
                catalogs.Show();((ComboBox)catalogs.Controls.Find("CatalogKind",true).Single()).SelectedIndex=Array.IndexOf(AssetCatalog.Kinds,"Location");catalogs.Controls.Find("CatalogValue",true).Single().Text="Office";((Button)catalogs.Controls.Find("AddCatalogValue",true).Single()).PerformClick();Save(catalogs,folder,"catalogs-en.png");catalogs.Close();
            }
            if(!catalog.Values("Location").Contains("Office") || store.Count()!=1)throw new Exception("Explicit catalog add created an asset or failed to persist");
            using(var movement=view.CreateMovement(original,"Assigned"))
            {
                movement.Show();movement.Controls.Find("Location",true).Single().Text="Office";movement.Controls.Find("Custodian",true).Single().Text="Ana Rivera";movement.Controls.Find("MovementReason",true).Single().Text="Initial handover";Save(movement,folder,"assign-asset-en.png");((Button)movement.Controls.Find("ConfirmMovement",true).Single()).PerformClick();movement.Close();
            }
        }
        var assigned=store.Get(original.Uid)!;if(assigned.Status!="Assigned" || assigned.Custodian!="Ana Rivera" || assigned.Notes!=original.Notes || store.Movements(assigned.Uid)[0].Note!="Initial handover" || store.Movements(assigned.Uid)[0].Action!="Assigned")throw new Exception("Guided assignment lost fields or reason");
        Reject<InvalidDataException>(()=>store.Transition(assigned,"Assigned","Office","","Invalid"));Reject<InvalidDataException>(()=>store.Transition(assigned,"Returned","","","Invalid"));
        var returned=store.Transition(assigned,"Returned","Storage","IGNORED","Returned to stock");if(returned.Status!="Available" || returned.Custodian!="" || store.Movements(returned.Uid)[0].Before?.Custodian!="Ana Rivera")throw new Exception("Return failed to clear/preserve assignment history");
        Reject<InvalidOperationException>(()=>store.Transition(assigned,"RepairStarted","Workshop","Service","Stale"));
        var repair=store.Transition(returned,"RepairStarted","Workshop","Service","Broken port");if(repair.Status!="Repair" || store.Movements(repair.Uid)[0].Note!="Broken port")throw new Exception("Repair workflow failed");
        Reject<InvalidDataException>(()=>store.Transition(repair,"Assigned","Never committed","Ana",new string('x',1001)));if(catalog.Values("Location").Contains("Never committed"))throw new Exception("Failed movement leaked catalog writes");
        repair=store.Transition(repair,"RepairStarted",repair.Location,repair.Custodian,"Progress checked");if(store.Movements(repair.Uid)[0].Note!="Progress checked")throw new Exception("Reason-only explicit movement was discarded");
        TestCsv(root,folder,history,store,repair);TestMigration(root,history,store);TestDashboard(root,folder);TestLookupAndBackups(root,folder,history,store);
        File.WriteAllText(Path.Combine(folder,"asset-operations-results.txt"),"PASS: serial recognition/explicit loading, New label caption, catalog uniqueness/spelling/translation, actual catalog/assignment forms, return/repair/reason/stale guards, administrative CSV round-trip/preview/rollback/concurrency, full filtered export, exact Dashboard card/group/date drill-down, schema-2 safety migration/legacy restore, catalog backup validation and durable/manual backup success/failure/missing-file recovery. No printer jobs.");
    }
    /// <summary>Checks full master CSV values and metadata, while proving errors/preview cannot partially update inventory or catalogs.</summary>
    private static void TestCsv(string root,string folder,HistoryStore history,AssetStore store,AssetRecord current)
    {
        string path=Path.Combine(root,"administration.csv");var proposed=current with {Status="Assigned",Location="Branch",Custodian="Ana Rivera",Notes="Updated; \"CSV\"\nsecond line"};
        AssetCsv.Export(path,[proposed,new AssetRecord{AssetType="Laptop",Brand="Dell",Model="XPS 13",Serial="000002",Location="Storage"}]);var plan=AssetCsv.Read(path,store);
        if(plan.Count!=2 || plan[0].Before!=current || plan[0].After.Notes!=proposed.Notes || plan[1].After.Serial!="000002")throw new Exception("Administrative CSV metadata/quotes/leading zeros lost");
        long count=store.Count(),events=history.Count();using(var view=new AssetsView(history,_=>{}))using(var preview=view.CreateImportPreview(plan)){preview.Show();Save(preview,folder,"asset-csv-preview-en.png");preview.Close();}
        if(store.Count()!=count || history.Count()!=events || store.Get(current.Uid)!=current)throw new Exception("CSV preview changed inventory");
        if(store.ImportAssets(plan)!=(1,1,0) || store.Get(current.Uid)!.Location!="Branch" || store.Movements(current.Uid)[0].Action!="ImportUpdated" || history.Count()!=events)throw new Exception("Administrative import wrote label events or wrong asset counts");
        store.ExportCsv(path,new());var same=AssetCsv.Read(path,store);if(store.ImportAssets(same)!=(0,0,2))throw new Exception("Unchanged import revised records");
        var fresh=store.Get(current.Uid)!;AssetCsv.Export(path,[new AssetRecord{AssetType="Uncommitted type",Serial="BEFORECONFLICT"},fresh with {Notes="Previewed"}]);var stale=AssetCsv.Read(path,store);store.Save(fresh with {Notes="Concurrent edit"});Reject<InvalidOperationException>(()=>store.ImportAssets(stale));
        if(store.FindBySerial("BEFORECONFLICT") is not null || new AssetCatalog(history).Values("AssetType").Contains("Uncommitted type"))throw new Exception("Stale CSV partially committed");
        AssetCsv.Export(path,[new AssetRecord{AssetType="CaseThing",Serial="CATRACE"}]);var catalogRace=AssetCsv.Read(path,store);new AssetCatalog(history).Add("AssetType","CASETHING");Reject<InvalidOperationException>(()=>store.ImportAssets(catalogRace));if(store.FindBySerial("CATRACE") is not null)throw new Exception("Catalog race changed reviewed spelling silently");
        AssetCsv.Export(path,[fresh]);Reject<InvalidDataException>(()=>AssetCsv.Read(path,store));
        AssetCsv.Export(path,[new AssetRecord{AssetType="Monitor",Serial="DUP"},new AssetRecord{AssetType="Monitor",Serial=" dup "}]);Reject<InvalidDataException>(()=>AssetCsv.Read(path,store));
        AssetCsv.Export(path,[new AssetRecord{Uid=Guid.NewGuid().ToString("N"),AssetType="Monitor",Serial="FOREIGN"}]);Reject<InvalidDataException>(()=>AssetCsv.Read(path,store));
        AssetCsv.Export(path,[new AssetRecord{AssetType="Monitor",Serial="INVALID",Status="Assigned"}]);Reject<InvalidDataException>(()=>AssetCsv.Read(path,store));
        AssetCsv.Export(path,[new AssetRecord{AssetType="Rollback vocabulary",Serial="FIRSTTX"},new AssetRecord{AssetType="Monitor",Serial="FAILTX"}]);var failure=AssetCsv.Read(path,store);
        Sql(history.DatabasePath,"CREATE TRIGGER fail_asset BEFORE INSERT ON assets WHEN NEW.serial_key='FAILTX' BEGIN SELECT RAISE(ABORT,'test rollback'); END");Reject<SqliteException>(()=>store.ImportAssets(failure));Sql(history.DatabasePath,"DROP TRIGGER fail_asset");
        if(store.FindBySerial("FIRSTTX") is not null || new AssetCatalog(history).Values("AssetType").Contains("Rollback vocabulary"))throw new Exception("CSV write failure leaked master/catalog changes");
        for(int i=0;i<53;i++)store.Save(new AssetRecord{AssetType="ExportPage",Serial="EP"+i.ToString("D3")});store.ExportCsv(path,new(Search:"ExportPage"));if(AssetCsv.Read(path,store).Count!=53)throw new Exception("Master export stopped at current UI page");
        Reject<ArgumentException>(()=>store.ExportCsv(history.DatabasePath,new()));
    }
    /// <summary>Builds a real schema-2 backup and proves protected migration/restore and semantic catalog validation.</summary>
    private static void TestMigration(string root,HistoryStore source,AssetStore store)
    {
        string directory=Path.Combine(root,"schema2");Directory.CreateDirectory(directory);string legacy=Path.Combine(directory,"inventory.db");source.Backup(legacy);
        Sql(legacy,"DROP TABLE count_actions;DROP TABLE count_entries;DROP TABLE count_sessions;DROP TABLE service_changes; DROP TABLE asset_services; DROP TABLE asset_catalog; ALTER TABLE asset_movements DROP COLUMN note; UPDATE asset_movements SET action='Updated' WHERE action IN ('Assigned','Returned','RepairStarted','ImportUpdated'); PRAGMA user_version=2;");string originalJson=Scalar(legacy,"SELECT record_json FROM assets ORDER BY id LIMIT 1");
        var upgraded=new HistoryStore(legacy);if(Scalar(legacy,"PRAGMA user_version")!="5" || Scalar(legacy,"SELECT record_json FROM assets ORDER BY id LIMIT 1")!=originalJson || new AssetCatalog(upgraded).Values("Brand").Count==0)throw new Exception("Schema-2 migration changed masters or lost catalogs");
        string safety=Directory.GetFiles(Path.Combine(directory,"backups"),"TagPrinter-before-catalogs-*.db").Single();if(Scalar(safety,"PRAGMA user_version")!="2")throw new Exception("Pre-catalog safety copy was upgraded");
        upgraded.RestoreBackup(safety,Path.Combine(directory,"restore-safety"));if(Scalar(safety,"PRAGMA user_version")!="2" || Scalar(legacy,"PRAGMA user_version")!="5")throw new Exception("Legacy restore modified source or failed staged migration");
        string invalid=Path.Combine(root,"invalid-catalog.db");source.Backup(invalid);Sql(invalid,"UPDATE asset_catalog SET name='Mismatch' WHERE rowid=(SELECT MIN(rowid) FROM asset_catalog)");long count=store.Count();Reject<InvalidDataException>(()=>source.RestoreBackup(invalid,Path.Combine(root,"rejected-safety")));if(store.Count()!=count)throw new Exception("Invalid catalog restore changed inventory");
        var catalog=new AssetCatalog(source);catalog.Add("Location","Unused saved option");string backup=Path.Combine(root,"complete.db");source.Backup(backup);catalog.Add("Location","Later option");source.RestoreBackup(backup,Path.Combine(root,"restore-safety"));if(!catalog.Values("Location").Contains("Unused saved option") || catalog.Values("Location").Contains("Later option") || store.Movements(store.FindBySerial("000001")!.Uid).All(m=>m.Note!="Initial handover"))throw new Exception("Full restore lost catalog/workflow reasons");
    }
    /// <summary>Checks clickable state/location/custodian scopes, including empty values and retained search/date bounds.</summary>
    private static void TestDashboard(string root,string folder)
    {
        var history=new HistoryStore(Path.Combine(root,"dashboard.db"));var store=new AssetStore(history);
        store.Save(new AssetRecord{AssetType="Monitor",Brand="Dell",Model="M",Serial="D1",Status="Assigned",Location="Office",Custodian="Ana"});store.Save(new AssetRecord{AssetType="Monitor",Brand="Dell",Model="M",Serial="D2",Location="Storage"});store.Save(new AssetRecord{AssetType="Laptop",Brand="HP",Model="X",Serial="D3",Status="Unspecified"});store.Save(new AssetRecord{AssetType="Monitor",Brand="Dell",Model="M",Serial="D4",Status="Retired",Location="Storage"});
        AssetFilter? opened=null;using var dashboard=new DashboardView(history,filter=>opened=filter);using var window=new AppWindow{Text="TagPrinter Dashboard",Width=1120,Height=720};window.Controls.Add(dashboard);window.Show();dashboard.SetFilters("Monitor",DateTime.Today,DateTime.Today);dashboard.OpenStatus("Assigned");
        if(opened is null || opened.Status!="Assigned" || opened.FromUtc is null || opened.ToUtc is null || opened.Search!="Monitor" || store.Count(opened)!=1)throw new Exception("State card lost exact date/search scope");
        dashboard.SetGrouping("Location");int storage=dashboard.GroupRows.ToList().FindIndex(g=>g.Location=="Storage");dashboard.OpenGroup(storage);if(opened!.Location!="Storage" || store.Count(opened)!=dashboard.GroupRows[storage].Count)throw new Exception("Location quantity did not match drill-down");
        using(var list=new AssetsView(history,_=>{})){list.ShowFilter(opened);if(list.VisibleCount!=1 || list.CurrentFilter.FromUtc!=opened.FromUtc)throw new Exception("Assets view dropped Dashboard scope");}
        dashboard.SetFilters("");dashboard.SetGrouping("Custodian");int empty=dashboard.GroupRows.ToList().FindIndex(g=>g.Custodian=="");dashboard.OpenGroup(empty);if(opened!.Custodian is null || opened.Custodian!="" || store.Count(opened)!=2)throw new Exception("Unassigned custodian group lost empty-vs-null meaning");
        Save(window,folder,"dashboard-operations-en.png");
        Ui.Language="es";dashboard.RefreshLanguage();Save(window,folder,"dashboard-operations-es.png");window.Close();Ui.Language="en";
    }
    /// <summary>Proves lookup is read-only until loading, checks persisted backup success/failure and uses the real manual command.</summary>
    private static void TestLookupAndBackups(string root,string folder,HistoryStore history,AssetStore store)
    {
        var asset=store.FindBySerial("000001")!;string settings=Path.Combine(root,"lookup-work.json");using var form=new MainForm(settings,AppConfig.CreateDefault(),history.DatabasePath);form.Show();form.Restore(new WorkState{Serial=" 000001 ",Brand="Draft brand",FontSizePt=15,Code=1});form.RefreshAssetMatch();
        if(!form.AssetMatchText.Contains(asset.Code) || !form.AssetMatchText.Contains(asset.Custodian) || form.CaptureWork().Brand!="Draft brand" || Descendants(form).OfType<Button>().Single(b=>b.Name=="NewLabel").Text!="New label")throw new Exception("Serial recognition replaced the draft or caption was not clarified");
        form.LoadMatchedAsset();if(form.CaptureWork().Brand!=asset.Brand || form.CaptureWork().AssetUid!=asset.Uid || form.CaptureWork().FontSizePt!=15)throw new Exception("Explicit asset loading lost identity/design");Save(form,folder,"serial-recognition-en.png");
        form.Restore(form.CaptureWork() with {Language="es"});form.RefreshAssetMatch();Save(form,folder,"serial-recognition-es.png");form.Restore(form.CaptureWork() with {Language="en"});
        File.WriteAllText(history.DatabasePath+".backup-status.json","{\"LastPath\":null}");form.RefreshBackupState();if(!form.BackupStateText.Contains("Could not read"))throw new Exception("Malformed backup metadata crashed or was hidden");
        string good=Path.Combine(root,"status-good.db");history.Backup(good);var success=BackupStatus.Read(history.DatabasePath);if(success.LastPath!=good || success.Error.Length>0)throw new Exception("Successful backup status missing");
        Reject<SqliteException>(()=>history.Backup(Path.Combine(root,"missing-folder","bad.db")));var failed=BackupStatus.Read(history.DatabasePath);if(failed.LastPath!=good || failed.Error.Length==0)throw new Exception("Backup failure lost prior successful path");
        form.RefreshBackupState();if(!form.BackupStateText.Contains("failed"))throw new Exception("Backup failure not visible");history.Backup(good);File.Delete(good);form.RefreshBackupState();if(!form.BackupStateText.Contains("no longer"))throw new Exception("Missing backup file not reported");
        var task=form.BackupNowAsync();var watch=Stopwatch.StartNew();while(!task.IsCompleted && watch.ElapsedMilliseconds<10000){Application.DoEvents();Thread.Sleep(10);}if(!task.IsCompleted)throw new Exception("Manual backup timed out");task.GetAwaiter().GetResult();var manual=BackupStatus.Read(history.DatabasePath);if(manual.Error.Length>0 || !File.Exists(manual.LastPath) || !Path.GetFileName(manual.LastPath).StartsWith("TagPrinter-manual-"))throw new Exception("Manual backup command/status failed");
        form.ShowAssets(new(Search:"Dell"));Application.DoEvents();if(!form.AssetListHasUsableSpace)throw new Exception("Assets toolbar hid the actual list");Save(form,folder,"asset-operations-en.png");form.Restore(form.CaptureWork() with {Language="es"});form.ShowAssets();Save(form,folder,"asset-operations-es.png");
        form.Size=new Size(980,720);Application.DoEvents();if(!form.AssetListHasUsableSpace || !form.AssetCommandsFit)throw new Exception("Small Spanish asset layout clipped commands or grid");Save(form,folder,"asset-operations-small-es.png");form.Close();
    }
    /// <summary>Executes synthetic schema mutation only against a supplied isolated test file.</summary>
    private static void Sql(string path,string sql){using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();}
    /// <summary>Reads fixture metadata without initializing/upgrading the backup under inspection.</summary>
    private static string Scalar(string path,string sql){using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;return cmd.ExecuteScalar()!.ToString()!;}
    /// <summary>Requires the expected validation/concurrency exception and fails if the action unexpectedly succeeds.</summary>
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    /// <summary>Renders real off-screen controls without changing desktop focus.</summary>
    private static void Save(Form form,string folder,string name){Application.DoEvents();using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(folder,name));}
    /// <summary>Finds controls by stable name rather than layout coordinates.</summary>
    private static IEnumerable<Control> Descendants(Control root){yield return root;foreach(Control child in root.Controls)foreach(var node in Descendants(child))yield return node;}
}
