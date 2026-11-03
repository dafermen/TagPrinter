namespace TagPrinter;

public partial class MainForm
{
    /// <summary>Captures the active catalog and every persisted editor preference; does not include event history.</summary>
    internal ConfigurationBundle CaptureConfiguration()=>new(){Catalog=appConfig,Work=CaptureWork()};

    /// <summary>Protects live files from accidentally being overwritten by a transfer export.</summary>
    internal void ExportConfigurationFile(string path)
    {
        string full=Path.GetFullPath(path);
        foreach(string protectedPath in new[]{catalogPath,config,history.DatabasePath,Path.Combine(AppContext.BaseDirectory,"TagPrinter.deps.json"),Path.Combine(AppContext.BaseDirectory,"TagPrinter.runtimeconfig.json"),Path.Combine(StartupDiagnostics.InstallationRoot,"integrity-manifest.xml")})
            if(full.Equals(Path.GetFullPath(protectedPath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(Ui.T("Elige un archivo nuevo para exportar; no sobrescribas los archivos activos.","Choose a new export file; do not overwrite live application files."));
        string payload=Path.GetFullPath(Path.Combine(StartupDiagnostics.InstallationRoot,"app"))+Path.DirectorySeparatorChar;
        if(full.StartsWith(payload,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Export outside the immutable app folder.");
        ConfigurationTransfer.Save(path,CaptureConfiguration());
    }

    /// <summary>Requests a transfer destination without recording a label event.</summary>
    private void ExportConfiguration()
    {
        using var dialog=new SaveFileDialog{Filter="TagPrinter configuration JSON|*.json",FileName="TagPrinter-configuration-"+DateTime.Now.ToString("yyyyMMdd")+".json",DefaultExt="json",AddExtension=true};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        ExportConfigurationFile(dialog.FileName);
        status.Text=Ui.T("Configuración completa exportada; incluye los datos del último trabajo. El historial se respalda desde Reportes.","Full configuration exported, including current asset data. Back up history separately in Reports.");
    }

    /// <summary>Validates and previews import scope before asking the user to replace settings.</summary>
    private void ImportConfiguration()
    {
        using var dialog=new OpenFileDialog{Filter="TagPrinter configuration JSON|*.json",CheckFileExists=true};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var incoming=ConfigurationTransfer.Load(dialog.FileName);
        string message=string.Format(Ui.T("Se reemplazarán tamaños ({0}), idioma, impresora, diseño y todos los valores del último trabajo. El historial no cambia. Se guardará un respaldo previo.\n\n¿Importar esta configuración?","This replaces media presets ({0}), language, printer, design and all last-work values. History is unchanged. A before-import backup will be saved.\n\nImport this configuration?"),incoming.Catalog.LabelSizes.Count);
        if(!printers.Items.Contains(incoming.Work.Printer) && incoming.Work.Printer.Length>0)message+="\n\n"+Ui.T("La impresora guardada no está instalada; quedará sin seleccionar.","The saved printer is not installed; it will be left unselected.");
        if(!fontFamily.Items.Contains(incoming.Work.FontFamily))message+="\n\n"+Ui.T("La fuente guardada no está instalada; se usará una fuente disponible.","The saved font is not installed; an available font will be used.");
        if(MessageBox.Show(this,message,"TagPrinter",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        string backup=ApplyConfiguration(incoming);
        status.Text=Ui.T("Configuración importada. Revisa la impresora y la vista previa. Respaldo: ","Configuration imported. Review printer and preview. Backup: ")+backup;
    }

    /// <summary>Backs up live editor values, persists a validated pair, refreshes catalog/UI and clears loaded-label provenance.</summary>
    /// <param name="incoming">Parsed transfer package; no file is reopened after confirmation.</param>
    /// <returns>Path of the automatic before-import full-configuration backup.</returns>
    internal string ApplyConfiguration(ConfigurationBundle incoming)
    {
        ConfigurationTransfer.Validate(incoming);
        var before=CaptureConfiguration();
        string backupDirectory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config))!,"configuration-backups");
        Directory.CreateDirectory(backupDirectory);
        string backup=Path.Combine(backupDirectory,"TagPrinter-before-import-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".json");
        ConfigurationTransfer.Save(backup,before);
        bool available=printers.Items.Contains(incoming.Work.Printer);
        string font=fontFamily.Items.Contains(incoming.Work.FontFamily)?incoming.Work.FontFamily:(fontFamily.Items.Contains("Arial")?"Arial":fontFamily.Items[0]!.ToString()!);
        var resolved=incoming with {Work=incoming.Work with {Printer=available?incoming.Work.Printer:"",FontFamily=font}};
        autosave.Stop();
        // Paired persistence stages files first. A write failure leaves the in-memory catalog/controls unchanged.
        ConfigurationTransfer.Install(resolved,catalogPath,config);
        bool active=ready;ready=false;
        try
        {
            appConfig=resolved.Catalog;sizes.Items.Clear();
            foreach(var preset in appConfig.LabelSizes)sizes.Items.Add(preset.Name);
            sizes.Items.Add(Ui.T("Personalizado","Custom"));
            printers.SelectedIndex=-1;Restore(resolved.Work);sourceHistoryId=null;
        }
        finally {ready=active;}
        Ui.Translate(this);UpdateChrome();ApplySize();SaveConfig();return backup;
    }
}
