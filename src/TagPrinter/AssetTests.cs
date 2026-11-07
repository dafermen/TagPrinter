using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace TagPrinter;

/// <summary>Tests canonical assets, stable history linkage, audit, optimistic edits and recoverable schema migration.</summary>
internal static class AssetTests
{
    /// <summary>Uses isolated files and real controls/SQLite without accessing live inventory or printers.</summary>
    internal static void Run(string folder)
    {
        Ui.Language="en";
        string root=Path.Combine(folder,"asset-admin-"+Guid.NewGuid());Directory.CreateDirectory(root);var history=new HistoryStore(Path.Combine(root,"inventory.db"));var assets=new AssetStore(history);
        var state=new WorkState{AssetType="Monitor",Brand="Dell",Model="H22203H",Serial="ASSET001"};
        long first=history.Add("ExportTag",state);history.Add("PrintSubmitted",state);history.Add("ExportCode",state);
        if(assets.Count()!=1 || history.Get(first)!.State!=state)throw new Exception("Reprints duplicated master assets or changed label snapshots");
        var original=assets.Query().Single();if(history.Get(first)!.AssetUid!=original.Uid || original.Code!="TP-00000001")throw new Exception("Missing stable asset/event identity");
        try{assets.Save(original with {Status="Assigned"});throw new Exception("Assignment without custodian accepted");}catch(InvalidDataException){}
        using(var view=new AssetsView(history,_=>{}))using(var editor=view.CreateEditor(original))
        {
            editor.Show();((TextBox)editor.Controls.Find("Location",true).Single()).Text="HQ / Floor 2";((TextBox)editor.Controls.Find("Custodian",true).Single()).Text="Alex Rivera";((TextBox)editor.Controls.Find("Notes",true).Single()).Text="Assigned for support";((ComboBox)editor.Controls.Find("Status",true).Single()).SelectedIndex=1;Application.DoEvents();
            using var image=new Bitmap(editor.Width,editor.Height);editor.DrawToBitmap(image,new Rectangle(Point.Empty,editor.Size));image.Save(Path.Combine(folder,"asset-editor-en.png"));
            editor.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b=>b.Name=="SaveAsset").PerformClick();editor.Close();
        }
        var assigned=assets.Get(original.Uid)!;
        if(assets.Movements(original.Uid).Count!=2 || assets.Get(original.Uid)!.Custodian!="Alex Rivera")throw new Exception("Assignment/audit missing");
        try{assets.Save(original with {Location="STALE"});throw new Exception("Stale asset edit accepted");}catch(InvalidOperationException){}
        try{assets.Save(new AssetRecord{AssetType="Laptop",Serial=" asset001 "});throw new Exception("Duplicate serial accepted");}catch(InvalidOperationException){}
        var changed=assets.Save(assigned with {Serial="RENAMED001",Status="Repair"});
        history.Add("ExportTag",state with {AssetUid=original.Uid});
        if(assets.Count()!=1 || assets.Get(original.Uid)!.Serial!="RENAMED001" || history.Query()[0].AssetUid!=original.Uid)throw new Exception("Historical reprint changed identity or master metadata");
        var noSerial=assets.Save(new AssetRecord{AssetType="HDMI cable",Location="Storage"});history.Add("ExportTag",new WorkState{AssetUid=noSerial.Uid,Serial="",ShowCode=false,AssetType="HDMI cable",Brand="",Model=""});
        if(assets.Count()!=2 || history.Query()[0].AssetUid!=noSerial.Uid)throw new Exception("No-serial asset linkage lost");
        long before=assets.Count();history.Add("ExportTag",new WorkState{FreeTextMode=true,FreeText="BOX",Serial="FREE"});if(assets.Count()!=before)throw new Exception("Free text created a master asset");
        var retired=assets.Save(changed with {Status="Retired"});var groups=assets.Dashboard("",null,null,false);
        if(groups.Groups.Sum(g=>g.Count)!=1 || assets.Count(status:"Retired")!=1)throw new Exception("Canonical Dashboard includes retired by default or loses state");
        if(assets.Query("Notes").Count!=0 || assets.Query("HQ / Floor 2").Single().Uid!=retired.Uid)throw new Exception("Asset search matched JSON keys or missed actual values");
        string snapshot=Path.Combine(root,"assets-backup.db");history.Backup(snapshot);var extra=assets.Save(new AssetRecord{AssetType="Laptop",Serial="EXTRA"});
        history.RestoreBackup(snapshot,Path.Combine(root,"backups"));if(assets.Get(extra.Uid)!=null || assets.Get(retired.Uid)!.Status!="Retired" || assets.Movements(retired.Uid).Count!=4)throw new Exception("Backup restore lost assets or movement history");
        using(var form=new MainForm(Path.Combine(root,"work.json"),AppConfig.CreateDefault(),history.DatabasePath))
        {
            form.Show();form.LoadAssetLabel(noSerial);if(!form.ShowingLabel || form.CaptureWork().ShowCode || form.CaptureWork().AssetUid!=noSerial.Uid)throw new Exception("Asset load printed/encoded a missing serial");
            form.LoadHistoryEvent(history.Get(first)!);form.ExportTagFile(Path.Combine(root,"historical-reprint.png"));if(assets.Count()!=2 || assets.Get(original.Uid)!.Serial!="RENAMED001")throw new Exception("Loading old history duplicated or overwrote the canonical asset");
            if(LabelProfile.Capture(form.CaptureWork()).AssetUid!="")throw new Exception("A profile retained master-asset identity");
            form.DuplicateAsset();if(form.CaptureWork().AssetUid!="")throw new Exception("Duplicate kept master identity");
            form.LoadAssetLabel(retired);form.ShowAssets();Application.DoEvents();using var en=new Bitmap(form.Width,form.Height);form.DrawToBitmap(en,new Rectangle(Point.Empty,form.Size));en.Save(Path.Combine(folder,"assets-en.png"));
            form.Restore(form.CaptureWork() with {Language="es"});form.ShowAssets();Application.DoEvents();using var es=new Bitmap(form.Width,form.Height);form.DrawToBitmap(es,new Rectangle(Point.Empty,form.Size));es.Save(Path.Combine(folder,"assets-es.png"));
            form.Close();
        }
        TestImportAndPages(root);
        TestLegacy(root);
        File.WriteAllText(Path.Combine(folder,"asset-admin-results.txt"),"PASS: one canonical asset across reprints, stable links after serial edits, assignment/state/location audit, stale-edit/duplicate guards, no-serial labels, Free text exclusion, retired Dashboard scope, literal field search, CSV identity isolation, global asset pages/sorting, corrupt asset-backup rejection, schema-2 backup/restore, bilingual UI and protected v1 migration. No physical print jobs.");
    }
    /// <summary>Proves import identity isolation, global page sorting and rejection of healthy SQLite with corrupt asset data.</summary>
    private static void TestImportAndPages(string root)
    {
        var history=new HistoryStore(Path.Combine(root,"import-pages.db"));var store=new AssetStore(history);
        var selected=store.Save(new AssetRecord{AssetType="Selected laptop",Serial="SELECTED"});
        string csv=Path.Combine(root,"asset-import.csv");File.WriteAllText(csv,InventoryCsv.Header+"\nMonitor;Dell;M;NEW001;;\nMonitor;Dell;M;NEW002;;\n");
        var rows=InventoryCsv.Read(csv,new WorkState{AssetUid=selected.Uid});if(rows.Any(s=>s.AssetUid.Length>0))throw new Exception("CSV inherited selected asset identity");
        var result=history.ImportInventory(rows.Select(s=>s with {AssetUid=selected.Uid}));
        if(result.Added!=2 || store.Count()!=3 || history.Query().Select(e=>e.AssetUid).Distinct().Count()!=2 || history.Query().Any(e=>e.AssetUid==selected.Uid))throw new Exception("Imported serials linked to the selected master");
        for(int i=0;i<53;i++)store.Save(new AssetRecord{AssetType="Page",Serial="PAGE"+i.ToString("D3"),Model="M"+i.ToString("D3")});
        var first=store.Query("PAGE",limit:50,sort:"Serial",descending:false);var second=store.Query("PAGE",offset:50,limit:50,sort:"Serial",descending:false);
        if(first.Count!=50 || second.Count!=3 || first[^1].Serial!="PAGE049" || second[0].Serial!="PAGE050" || first.Select(a=>a.Uid).Intersect(second.Select(a=>a.Uid)).Any())throw new Exception("Asset SQL pagination/global sorting failed");
        string backup=Path.Combine(root,"invalid-assets.db");history.Backup(backup);
        using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Pooling=false}.ToString()))
        {
            c.Open();using var cmd=c.CreateCommand();cmd.CommandText="UPDATE assets SET record_json=json_set(record_json,'$.Status','Invalid') WHERE uid=$uid";cmd.Parameters.AddWithValue("$uid",selected.Uid);cmd.ExecuteNonQuery();
        }
        bool rejected=false;try{history.RestoreBackup(backup,Path.Combine(root,"reject-safety"));}catch(InvalidDataException){rejected=true;}
        if(!rejected || store.Get(selected.Uid)!.Status!="Available" || store.Count()!=56)throw new Exception("Invalid asset backup replaced live administration");
    }
    /// <summary>Builds an actual version-1 fixture and checks an untouched safety copy plus latest-active-serial migration.</summary>
    private static void TestLegacy(string root)
    {
        string dir=Path.Combine(root,"legacy");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"TagPrinter.db");
        using(var c=new SqliteConnection("Data Source="+path))
        {
            c.Open();using var cmd=c.CreateCommand();cmd.CommandText="""
                CREATE TABLE events(id INTEGER PRIMARY KEY AUTOINCREMENT,created_utc TEXT NOT NULL,action TEXT NOT NULL,serial TEXT NOT NULL,asset_type TEXT NOT NULL,brand TEXT NOT NULL,model TEXT NOT NULL,part_number TEXT NOT NULL,mac_address TEXT NOT NULL,state_json TEXT NOT NULL,output_path TEXT NOT NULL,source_event_id INTEGER REFERENCES events(id));
                CREATE TABLE trash(event_id INTEGER PRIMARY KEY REFERENCES events(id),deleted_utc TEXT NOT NULL);
                PRAGMA user_version=1;
                """;cmd.ExecuteNonQuery();
            foreach(var item in new[]{new WorkState{Serial="míg001",Model="Old",Code=1},new WorkState{Serial="MÍG001",Model="Latest",Code=1},new WorkState{Serial="TRASH001",Model="Recycled"}})
            {
                cmd.Parameters.Clear();cmd.CommandText="INSERT INTO events(created_utc,action,serial,asset_type,brand,model,part_number,mac_address,state_json,output_path) VALUES($date,'ExportTag',$serial,$type,$brand,$model,'','',$json,'')";cmd.Parameters.AddWithValue("$date",DateTimeOffset.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$serial",item.Serial);cmd.Parameters.AddWithValue("$type",item.AssetType);cmd.Parameters.AddWithValue("$brand",item.Brand);cmd.Parameters.AddWithValue("$model",item.Model);cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(item));cmd.ExecuteNonQuery();
            }
            cmd.Parameters.Clear();cmd.CommandText="INSERT INTO trash VALUES(3,'2026-11-06T00:00:00Z')";cmd.ExecuteNonQuery();
        }
        var migrated=new HistoryStore(path);var assets=new AssetStore(migrated);if(assets.Count()!=1 || assets.Query().Single().Model!="Latest" || assets.Query().Single().Status!="Unspecified" || migrated.Count()!=2 || migrated.Get(1)!.AssetUid!=migrated.Get(2)!.AssetUid)throw new Exception("Legacy migration changed events, duplicated assets or picked the wrong snapshot");
        string backup=Directory.GetFiles(Path.Combine(dir,"backups"),"TagPrinter-before-assets-*.db").Single();
        using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString())){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA user_version";if(Convert.ToInt32(cmd.ExecuteScalar())!=1)throw new Exception("Pre-migration safety copy was upgraded");}
        new HistoryStore(path);if(Directory.GetFiles(Path.Combine(dir,"backups"),"TagPrinter-before-assets-*.db").Length!=1)throw new Exception("Migration ran again at every startup");
        migrated.RestoreBackup(backup,Path.Combine(dir,"restore-safety"));if(new AssetStore(migrated).Count()!=1 || migrated.Count()!=2)throw new Exception("Version-1 restore did not normalize assets safely");
    }
}
