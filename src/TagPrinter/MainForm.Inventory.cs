using System.Text;
using System.Text.Json;
namespace TagPrinter;

public partial class MainForm
{
    // Profiles belong to config.json; backups belong beside the user database, outside the portable.
    private readonly ComboBox profileName=new(){Width=170,DropDownStyle=ComboBoxStyle.DropDown};
    private readonly CheckBox automaticBackup=new(){AutoSize=true};
    private readonly Label duplicateSerial=new(){AutoSize=true,ForeColor=Color.DarkGoldenrod,MaximumSize=new Size(330,0)};
    private readonly List<(Button Button,string Es,string En)> inventoryButtons=[];
    private readonly System.Windows.Forms.Timer backupTimer=new(){Interval=3600000},duplicateTimer=new(){Interval=400};
    private bool backupRunning,toolsReady;
    private string BackupDirectory=>Path.Combine(Path.GetDirectoryName(history.DatabasePath)!,"backups");

    /// <summary>Adds reusable profiles, CSV tools, restore and automatic backup without expanding collapsed sections.</summary>
    private void InitializeInventoryTools()
    {
        Button Make(string es,string en,Action action){var b=new Button{AutoSize=true};inventoryButtons.Add((b,es,en));b.Click+=(_,_)=>Guard(action);return b;}
        var profiles=new FlowLayoutPanel{AutoSize=true,MaximumSize=new Size(330,0)};profiles.Controls.Add(new Label{Text=Ui.T("Perfil","Profile"),AutoSize=true,Padding=new Padding(0,5,0,0)});profiles.Controls.Add(profileName);
        profiles.Controls.AddRange([Make("Guardar perfil","Save profile",()=>SaveProfile(profileName.Text)),Make("Aplicar perfil","Apply profile",()=>ApplyProfile(profileName.Text)),Make("Eliminar perfil","Delete profile",()=>DeleteProfile(profileName.Text))]);
        templateSection.Body.Controls.Add(profiles);templateSection.Body.Controls.SetChildIndex(profiles,0);
        var tools=new FlowLayoutPanel{AutoSize=true,MaximumSize=new Size(330,0)};tools.Controls.AddRange([Make("Importar inventario CSV…","Import inventory CSV…",ImportInventoryDialog),Make("Plantilla CSV…","CSV template…",SaveInventoryTemplate),Make("Restaurar base de datos…","Restore database…",RestoreDatabaseDialog)]);settingsSection.Body.Controls.Add(tools);
        automaticBackup.Checked=appConfig.AutomaticBackups;settingsSection.Body.Controls.Add(automaticBackup);
        automaticBackup.CheckedChanged+=(_,_)=>{if(!toolsReady)return;Guard(()=>SaveCatalog(appConfig with {AutomaticBackups=automaticBackup.Checked}));RefreshInventoryTools();};
        var duplicate=Make("Duplicar etiqueta","Duplicate label",DuplicateAsset);assetSection.Body.Controls.Add(duplicate);assetSection.Body.Controls.Add(duplicateSerial);
        loadSerialAsset.Click+=(_,_)=>Guard(LoadMatchedAsset);assetSection.Body.Controls.Add(loadSerialAsset);
        backupNow=Make("Respaldar ahora","Back up now",()=>_ = BackupNowAsync());settingsSection.Body.Controls.Add(backupNow);settingsSection.Body.Controls.Add(backupState);
        settingsSection.Body.VisibleChanged+=(_,_)=>{if(settingsSection.Body.Visible)RefreshBackupState();};
        freeTextMode.CheckedChanged+=(_,_)=>{duplicate.Enabled=!freeTextMode.Checked;if(toolsReady)duplicateTimer.Start();};
        serial.TextChanged+=(_,_)=>{duplicateTimer.Stop();if(toolsReady)duplicateTimer.Start();};
        duplicateTimer.Tick+=(_,_)=>{duplicateTimer.Stop();RefreshDuplicateWarning();};
        backupTimer.Tick+=(_,_)=>_ = AutomaticBackupAsync();Shown+=(_,_)=>{if(!isolatedPrinting){backupTimer.Start();_ = AutomaticBackupAsync();}};
        toolsReady=true;RefreshInventoryTools();
    }
    /// <summary>Translates added controls and reloads available names without changing the editor.</summary>
    private void RefreshInventoryTools()
    {
        if(!toolsReady)return;
        foreach(var item in inventoryButtons)item.Button.Text=Ui.T(item.Es,item.En);
        automaticBackup.Text=Ui.T("Respaldos automáticos diarios (conservar 10)","Daily automatic backups (keep 10)");
        bool active=toolsReady;toolsReady=false;automaticBackup.Checked=appConfig.AutomaticBackups;toolsReady=active;
        string selected=profileName.Text;profileName.Items.Clear();profileName.Items.AddRange(appConfig.Profiles.Select(p=>(object)p.Name).ToArray());profileName.Text=selected;
        loadSerialAsset.Text=Ui.T("Cargar activo","Load asset");RefreshDuplicateWarning();RefreshAssetSuggestions();RefreshBackupState();
    }
    /// <summary>Writes validated catalog settings atomically; failed writes leave the in-memory catalog unchanged.</summary>
    private void SaveCatalog(AppConfig candidate)
    {
        candidate.Validate();byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(candidate,new JsonSerializerOptions{WriteIndented=true});
        if(bytes.Length>ConfigurationTransfer.MaxBytes)throw new InvalidDataException("Catalog exceeds 1 MB.");
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);string temp=catalogPath+"."+Guid.NewGuid()+".tmp";
        try{File.WriteAllBytes(temp,bytes);File.Move(temp,catalogPath,true);appConfig=candidate;}finally{if(File.Exists(temp))File.Delete(temp);}
        RefreshInventoryTools();
    }
    /// <summary>Saves only formatting; replacing an existing name requires explicit UI confirmation.</summary>
    internal void SaveProfile(string name,bool overwrite=false)
    {
        name=name.Trim();if(name.Length is <1 or >60)throw new ArgumentException(Ui.T("Escribe un nombre de perfil (1–60 caracteres).","Enter a profile name (1–60 characters)."));
        var old=appConfig.Profiles.Find(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
        if(old is not null && !overwrite && MessageBox.Show(this,Ui.T("¿Reemplazar el perfil existente?","Replace the existing profile?"),"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        var profiles=appConfig.Profiles.Where(p=>!p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).Append(new LabelProfile(name,LabelProfile.Capture(CaptureWork()))).ToList();SaveCatalog(appConfig with {Profiles=profiles});profileName.Text=name;
    }
    /// <summary>Applies layout without replacing asset identifiers, language, printer or copies.</summary>
    internal void ApplyProfile(string name)
    {
        var profile=appConfig.Profiles.Find(p=>p.Name.Equals(name.Trim(),StringComparison.OrdinalIgnoreCase))??throw new ArgumentException(Ui.T("Selecciona un perfil guardado.","Select a saved profile."));
        Restore(LabelProfile.Apply(CaptureWork(),profile.Design));SaveConfig();
    }
    /// <summary>Deletes only a named profile; the current editor/design remains unchanged.</summary>
    private void DeleteProfile(string name)
    {
        if(!appConfig.Profiles.Any(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))return;
        if(MessageBox.Show(this,Ui.T("¿Eliminar este perfil?","Delete this profile?"),"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        SaveCatalog(appConfig with {Profiles=appConfig.Profiles.Where(p=>!p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToList()});profileName.Text="";
    }
    /// <summary>Starts another asset with the same type/brand/model/design, clearing potentially unique identifiers and provenance.</summary>
    internal void DuplicateAsset()
    {
        var state=CaptureWork();sourceHistoryId=null;selectedAssetUid=null;Restore(state with {AssetUid="",Serial="",PartNumber="",MacAddress=""});assetSection.Expanded=true;SaveConfig();serial.Focus();
    }
    /// <summary>Warns without blocking legitimate reprints; free text and recycled events are excluded.</summary>
    private void RefreshDuplicateWarning()=>RefreshAssetMatch();
    /// <summary>Creates a consistent daily copy off-thread; an unsuccessful attempt is visible and can retry on the next tick.</summary>
    private async Task AutomaticBackupAsync()
    {
        if(backupRunning || !appConfig.AutomaticBackups || IsDisposed)return;backupRunning=true;RefreshBackupState();
        try{await Task.Run(()=>history.AutomaticBackup(BackupDirectory));}
        catch(Exception e){if(!IsDisposed)status.Text=Ui.T("Falló el respaldo automático: ","Automatic backup failed: ")+e.Message;}
        finally{backupRunning=false;RefreshBackupState();}
    }
    /// <summary>Writes a sample semicolon/UTF-8 CSV rather than guessing the user's input columns.</summary>
    private void SaveInventoryTemplate()
    {
        using var d=new SaveFileDialog{Filter="CSV|*.csv",FileName="TagPrinter-import-template.csv"};
        if(d.ShowDialog(this)==DialogResult.OK)File.WriteAllText(d.FileName,InventoryCsv.Header+"\r\nMonitor;Dell;H22203H;ABC123;ZYX123;00:1A:2B:3C:4D:5E\r\n",new UTF8Encoding(true));
    }
    /// <summary>Validates all CSV rows and previews them before an all-or-nothing import; no output is printed.</summary>
    private void ImportInventoryDialog()
    {
        if(printBusy)throw new InvalidOperationException(Ui.T("Espera a que termine el envío de impresión.","Wait for the print submission/tracking to finish."));
        using var d=new OpenFileDialog{Filter="CSV|*.csv"};if(d.ShowDialog(this)!=DialogResult.OK)return;
        var rows=InventoryCsv.Read(d.FileName,CaptureWork());
        using var previewDialog=new AppWindow{Text=Ui.T("Vista previa de importación","Import preview"),Width=850,Height=520,StartPosition=FormStartPosition.CenterParent};
        var grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,DataSource=rows.Select(s=>new{s.AssetType,s.Brand,s.Model,s.Serial,s.PartNumber,s.MacAddress}).ToList()};
        var panel=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=76};var skip=new CheckBox{AutoSize=true,Checked=true,Text=Ui.T("Omitir seriales existentes o repetidos en el archivo","Skip existing serials and duplicates within the file")};
        var commit=new Button{AutoSize=true,Text=Ui.T("Importar","Import"),DialogResult=DialogResult.OK};panel.Controls.AddRange([new Label{AutoSize=true,Text=$"{rows.Count} "+Ui.T("registros. No se imprimirán etiquetas.","records. No labels will be printed.")},skip,commit]);previewDialog.Controls.Add(grid);previewDialog.Controls.Add(panel);
        FluentIcons.ApplyTree(previewDialog);if(previewDialog.ShowDialog(this)!=DialogResult.OK)return;
        history.AutomaticBackup(BackupDirectory,true);var result=history.ImportInventory(rows,skip.Checked);reports?.RefreshData();RefreshDuplicateWarning();
        MessageBox.Show(this,string.Format(Ui.T("Importados: {0}. Omitidos: {1}.","Imported: {0}. Skipped: {1}."),result.Added,result.Skipped),"TagPrinter");
    }
    /// <summary>Replaces inventory after validation, explicit confirmation and a preserved pre-restore safety copy.</summary>
    private void RestoreDatabaseDialog()
    {
        if(printBusy || backupRunning || countsView?.HasPendingWork==true)throw new InvalidOperationException(Ui.T("Espera a que termine la operación actual.","Wait for the current operation to finish."));
        using var d=new OpenFileDialog{Filter="SQLite|*.db"};if(d.ShowDialog(this)!=DialogResult.OK)return;
        if(MessageBox.Show(this,Ui.T("Se reemplazarán los activos, tomas físicas, mantenimientos, garantías, catálogos, movimientos, historial de etiquetas y papelera. Se guardará un respaldo previo. Cierra otras instancias de TagPrinter. ¿Restaurar?","This replaces assets, physical counts, maintenance, warranties, catalogs, movements, label history and the recycle bin. A safety backup will be saved. Close other TagPrinter instances. Restore?"),"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        string backup=history.RestoreBackup(d.FileName,BackupDirectory);sourceHistoryId=null;selectedAssetUid=null;servicesView?.ShowAsset(null);countsView?.Reset();SaveConfig();reports?.RefreshData();assetsView?.RefreshData();RefreshDuplicateWarning();status.Text=Ui.T("Base restaurada. Respaldo previo: ","Database restored. Safety backup: ")+backup;
    }
    /// <summary>Stops local timers; any already-running backup owns and closes its own SQLite connections.</summary>
    private void DisposeInventoryTools(){backupTimer.Stop();backupTimer.Dispose();duplicateTimer.Stop();duplicateTimer.Dispose();}
}
