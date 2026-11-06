using Microsoft.Data.Sqlite;
namespace TagPrinter;

/// <summary>Validates imports, SQL paging, recoverable backups and design-only profiles with isolated user data.</summary>
internal static class InventoryToolsTests
{
    /// <summary>Runs transactional and UI checks without submitting or querying printers.</summary>
    internal static void Run(string folder)
    {
        string root=Path.Combine(folder,"inventory-tools-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var store=new HistoryStore(Path.Combine(root,"events.db"));string csv=Path.Combine(root,"inventory.csv");
        
        // Use the application exporter to exercise real quotes/newlines and preserve the leading-zero serial.
        long seed=store.Add("ExportTag",new WorkState{Serial="000001",Model="Model; \"quoted\"\nsecond line"});
        HistoryStore.ExportCsv(csv,store.Query());var rows=InventoryCsv.Read(csv,new WorkState());
        if(rows.Single().Serial!="000001" || rows[0].Model!="Model; \"quoted\"\nsecond line")throw new Exception("CSV quoting or leading zeros lost");
        var result=store.ImportInventory([rows[0],rows[0] with {Serial="NEW"},rows[0] with {Serial="new"}]);
        if(result!=(1,2) || !store.HasSerial(" new ") || store.Query(new(Action:"Imported")).Count!=1)throw new Exception("Import duplicate handling/action incorrect");
        using(var c=new SqliteConnection("Data Source="+store.DatabasePath)){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TRIGGER reject_import BEFORE INSERT ON events WHEN NEW.serial='FAIL' BEGIN SELECT RAISE(ABORT,'test rollback'); END";cmd.ExecuteNonQuery();}
        long count=store.Count();try{store.ImportInventory([rows[0] with {Serial="FIRST"},rows[0] with {Serial="FAIL"}]);throw new Exception("Expected transactional import failure");}catch(SqliteException){}
        if(store.Count()!=count || store.HasSerial("FIRST"))throw new Exception("Import partially committed");
        using(var c=new SqliteConnection("Data Source="+store.DatabasePath)){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="DROP TRIGGER reject_import";cmd.ExecuteNonQuery();}
        var batch=Enumerable.Range(0,137).Select(i=>new WorkState{Serial=$"SER{i:000}",AssetType="Monitor",Brand="Dell",Model="M"}).ToList();store.ImportInventory(batch);
        var first=store.Query(limit:50,sort:"Serial",descending:false);var second=store.Query(limit:50,offset:50,sort:"Serial",descending:false);
        if(first.Count!=50 || second.Count!=50 || first.Select(e=>e.Id).Intersect(second.Select(e=>e.Id)).Any())throw new Exception("Pages overlap or exceed the SQL limit");
        if(store.Count(new(GroupType:"Monitor",GroupBrand:"Dell",GroupModel:"M"))!=137)throw new Exception("Grouped SQL count incorrect");
        using(var report=new ReportsView(store,_=>{})){report.RefreshData();if(report.VisibleRows().Count!=50)throw new Exception("Report loaded whole history");report.GoToPage(2);if(report.VisibleRows().Count!=39)throw new Exception("Report last page incorrect");}
        using(var view=new MainForm(Path.Combine(root,"page-ui","work.json"),AppConfig.CreateDefault(),store.DatabasePath))
        {
            view.Show();view.ShowReports();Application.DoEvents();using var screenshot=new Bitmap(view.Width,view.Height);view.DrawToBitmap(screenshot,view.ClientRectangle);screenshot.Save(Path.Combine(folder,"reports-paging-en.png"));view.Close();
        }
        string backups=Path.Combine(root,"backups");string saved=store.AutomaticBackup(backups)!;
        if(!File.Exists(saved) || store.AutomaticBackup(backups)!=null)throw new Exception("Daily backup repeated or missing");
        for(int i=0;i<12;i++)store.AutomaticBackup(backups,true);
        if(Directory.GetFiles(backups,"TagPrinter-auto-*.db").Length!=10)throw new Exception("Automatic retention incorrect");
        string snapshot=Path.Combine(root,"restore.db");store.Backup(snapshot);long expected=store.Count();store.Add("ExportTag",new WorkState{Serial="LATER"});store.SetDeleted([seed],true);
        string before=store.RestoreBackup(snapshot,backups);
        if(store.Count()!=expected || store.HasSerial("LATER") || !File.Exists(before) || new HistoryStore(before).Count()!=expected || !new HistoryStore(before).HasSerial("LATER"))throw new Exception("Restore did not replace inventory/preserve pre-restore copy");
        string corrupt=Path.Combine(root,"invalid.db");File.WriteAllText(corrupt,"not sqlite");
        try{store.RestoreBackup(corrupt,backups);throw new Exception("Corrupt backup accepted");}catch(SqliteException){}
        if(store.Count()!=expected)throw new Exception("Invalid restore changed live DB");
        string badState=Path.Combine(root,"bad-state.db");store.Backup(badState);
        using(var c=new SqliteConnection("Data Source="+badState)){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="UPDATE events SET state_json=json_set(state_json,'$.AssetType',NULL) WHERE id=(SELECT MIN(id) FROM events)";cmd.ExecuteNonQuery();}
        try{store.RestoreBackup(badState,backups);throw new Exception("Invalid saved label accepted in backup");}catch(InvalidDataException){}
        if(store.Count()!=expected)throw new Exception("Invalid label restore changed inventory");
        File.WriteAllText(csv,"Serial;Brand\nVALID;Dell\n;HP\n");try{InventoryCsv.Read(csv,new());throw new Exception("Invalid import accepted");}catch(InvalidDataException){}
        string work=Path.Combine(root,"work.json");
        using(var form=new MainForm(work,AppConfig.CreateDefault()))
        {
            form.Show();form.Restore(new WorkState{Serial="ORIGINAL",AssetType="Laptop",Brand="Dell",Model="M",PartNumber="P",MacAddress="MAC",FontSizePt=18,CenterText=true,Code=1});form.SaveProfile("Laptop QR",true);
            form.Restore(form.CaptureWork() with {Serial="CURRENT",FontSizePt=8,CenterText=false});form.ApplyProfile("Laptop QR");
            if(form.CaptureWork().Serial!="CURRENT" || form.CaptureWork().FontSizePt!=18 || !form.CaptureWork().CenterText)throw new Exception("Profile replaced identity or lost design");
            form.DuplicateAsset();var duplicate=form.CaptureWork();if(duplicate.Serial!="" || duplicate.PartNumber!="" || duplicate.MacAddress!="" || duplicate.Brand!="Dell" || duplicate.Model!="M")throw new Exception("Duplicate retained unique identifiers");
            if(form.CaptureConfiguration().Catalog.Profiles.Single().Design.Serial!="")throw new Exception("Profile leaked asset identity");
            string transfer=Path.Combine(Path.GetTempPath(),"TagPrinter-profiles-"+Guid.NewGuid()+".json");form.ExportConfigurationFile(transfer);var bundle=ConfigurationTransfer.Load(transfer);File.Delete(transfer);
            using(var target=new MainForm(Path.Combine(root,"transfer-target","work.json"),AppConfig.CreateDefault()))
            {
                target.ApplyConfiguration(bundle);if(target.CaptureConfiguration().Catalog.Profiles.Count!=1)throw new Exception("Configuration transfer lost profiles");
                target.ApplyProfile("Laptop QR");if(target.CaptureWork().FontSizePt!=18)throw new Exception("Transferred profile could not be applied");
            }
            form.Restore(duplicate with {Serial="NEWASSET001",Language="en"});form.assetSection.Expanded=false;form.settingsSection.Expanded=true;Application.DoEvents();using var shot=new Bitmap(form.Width,form.Height);form.DrawToBitmap(shot,form.ClientRectangle);shot.Save(Path.Combine(folder,"inventory-tools-en.png"));
            form.settingsSection.Expanded=false;form.templateSection.Expanded=true;Application.DoEvents();using var profiles=new Bitmap(form.Width,form.Height);form.DrawToBitmap(profiles,form.ClientRectangle);profiles.Save(Path.Combine(folder,"label-profiles-en.png"));
            form.templateSection.Expanded=false;form.settingsSection.Expanded=true;form.Restore(form.CaptureWork() with {Language="es"});Application.DoEvents();using var spanish=new Bitmap(form.Width,form.Height);form.DrawToBitmap(spanish,form.ClientRectangle);spanish.Save(Path.Combine(folder,"inventory-tools-es.png"));form.SaveConfig();form.Close();
        }
        if(AppConfig.Load(Path.Combine(root,"catalog.json")).Profiles.Count!=1)throw new Exception("Profile not durable");
        File.WriteAllText(Path.Combine(folder,"inventory-tools-results.txt"),"PASS: CSV quotes/leading zeros, duplicates, transaction rollback, SQL pages/global order/group count, UI page bounds, daily/retained backups, restore/safety copy/invalid preservation, reusable design profiles and cleared-identity duplication. No printing performed.");
    }
}
