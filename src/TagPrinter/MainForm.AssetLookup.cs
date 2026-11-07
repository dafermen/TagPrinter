namespace TagPrinter;

public partial class MainForm
{
    // Recognition is informational until Load asset is clicked; typing must never overwrite the draft.
    private readonly Button loadSerialAsset=new(){AutoSize=true,Visible=false,Name="LoadSerialAsset"};
    private readonly Label backupState=new(){AutoSize=true,MaximumSize=new Size(330,0),Name="BackupState"};
    private Button? backupNow;
    private AssetRecord? serialMatch;
    /// <summary>Read-only lookup feedback used by isolated integration tests.</summary>
    internal string AssetMatchText=>duplicateSerial.Text;
    /// <summary>Current persisted backup feedback, independent of footer messages.</summary>
    internal string BackupStateText=>backupState.Text;
    /// <summary>Recognizes the canonical serial including retired records and offers an explicit load action.</summary>
    internal void RefreshAssetMatch()
    {
        if(!toolsReady || IsDisposed)return;
        serialMatch=null;loadSerialAsset.Visible=false;duplicateSerial.Text="";
        if(freeTextMode.Checked)return;
        try
        {
            serialMatch=new AssetStore(history).FindBySerial(serial.Text);
            if(serialMatch is {} item)
            {
                duplicateSerial.Text=item.Code+" · "+AssetStore.StatusCaption(item.Status)+Environment.NewLine+
                    Ui.T("Ubicación: ","Location: ")+(item.Location.Length>0?item.Location:Ui.T("Sin indicar","Not set"))+Environment.NewLine+
                    Ui.T("Responsable: ","Custodian: ")+(item.Custodian.Length>0?item.Custodian:Ui.T("Sin indicar","Not set"));
                duplicateSerial.ForeColor=item.Status=="Retired"?Color.Firebrick:Color.FromArgb(35,65,100);loadSerialAsset.Visible=true;
            }
            else if(history.HasSerial(serial.Text)){duplicateSerial.ForeColor=Color.DarkGoldenrod;duplicateSerial.Text=Ui.T("Serial encontrado en el historial. Puedes cargar su etiqueta desde Reportes.","Serial found in label history. Load its label from Reports.");}
        }
        catch(Exception e){duplicateSerial.ForeColor=Color.Firebrick;duplicateSerial.Text=Ui.T("No se pudo verificar el serial: ","Could not check serial: ")+e.Message;}
    }
    /// <summary>Resolves again at click time to avoid loading a stale serial match after a rapid edit.</summary>
    internal void LoadMatchedAsset()
    {
        var asset=new AssetStore(history).FindBySerial(serial.Text)??throw new InvalidOperationException(Ui.T("No hay un activo para este serial.","No asset matches this serial."));LoadAssetLabel(asset);RefreshAssetMatch();
    }
    /// <summary>Offers catalog spellings without translating or replacing user-entered label values.</summary>
    private void RefreshAssetSuggestions()
    {
        var catalog=new AssetCatalog(history);
        foreach(var pair in new[]{(assetType,"AssetType"),(brand,"Brand"),(model,"Model")})
        {
            var options=new AutoCompleteStringCollection();options.AddRange(catalog.Values(pair.Item2).ToArray());pair.Item1.AutoCompleteMode=AutoCompleteMode.SuggestAppend;pair.Item1.AutoCompleteSource=AutoCompleteSource.CustomSource;pair.Item1.AutoCompleteCustomSource=options;
        }
    }
    /// <summary>Shows the last durable backup and most recent failure; missing backup files are called out explicitly.</summary>
    internal void RefreshBackupState()
    {
        if(IsDisposed || Disposing)return;
        var info=BackupStatus.Read(history.DatabasePath);string text=Ui.T("Último respaldo: ","Last backup: ");
        text+=DateTimeOffset.TryParse(info.LastSuccessUtc,out var time)?time.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"):Ui.T("Aún no registrado","Not recorded yet");
        if(info.LastPath.Length>0)text+=Environment.NewLine+info.LastPath;
        bool missing=info.LastPath.Length>0 && !File.Exists(info.LastPath);
        if(missing)text+=Environment.NewLine+Ui.T("El archivo ya no está en esa ubicación.","File is no longer at that location.");
        if(info.Error.Length>0)text+=Environment.NewLine+Ui.T("Último intento fallido: ","Last attempt failed: ")+info.Error;
        if(backupRunning)text+=Environment.NewLine+Ui.T("Creando respaldo…","Creating backup…");
        backupState.Text=text;backupState.ForeColor=missing||info.Error.Length>0?Color.Firebrick:Color.DimGray;if(backupNow is not null)backupNow.Enabled=!backupRunning;
    }
    /// <summary>Creates an explicit protected manual copy off-thread; automatic retention does not prune these copies.</summary>
    internal async Task BackupNowAsync()
    {
        if(backupRunning || IsDisposed)return;backupRunning=true;RefreshBackupState();
        try
        {
            Directory.CreateDirectory(BackupDirectory);string target=Path.Combine(BackupDirectory,"TagPrinter-manual-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".db");await Task.Run(()=>history.Backup(target));
            if(!IsDisposed)status.Text=Ui.T("Respaldo creado: ","Backup created: ")+target;
        }
        catch(Exception error){BackupStatus.Record(history.DatabasePath,BackupDirectory,error);if(!IsDisposed)status.Text=Ui.T("Falló el respaldo: ","Backup failed: ")+error.Message;}
        finally{backupRunning=false;RefreshBackupState();}
    }
}
