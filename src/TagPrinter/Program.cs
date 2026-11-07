namespace TagPrinter;
/// <summary>Windows entry point; separates verification from interactive startup.</summary>
internal static class Program
{
    /// <summary>
    /// Initializes WinForms, handles --self-test or loads config and starts UI with separate
    /// configuration/data failure messages.
    /// </summary>
    /// <param name="args">Command-line arguments; --self-test selects verification.</param>
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Contains("--self-test"))
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            // Keep a real UI message loop alive for async continuations across many synthetic windows.
            // The off-screen host prevents closing a test dialog from disposing its synchronization context.
            using var host=new AppWindow();
            host.Shown+=(_,_)=>
            {
                try
                {
                    if(args.Contains("--counts-only")){string folder=Path.Combine(AppContext.BaseDirectory,"verification");Directory.CreateDirectory(folder);CountTests.Run(folder);}
                    else if(args.Contains("--services-only")){string folder=Path.Combine(AppContext.BaseDirectory,"verification");Directory.CreateDirectory(folder);ServiceTests.Run(folder);}
                    else if(args.Contains("--performance-only")){string folder=Path.Combine(AppContext.BaseDirectory,"verification");Directory.CreateDirectory(folder);PerformanceTests.Run(folder);}
                    else if(args.Contains("--asset-operations-only")){string folder=Path.Combine(AppContext.BaseDirectory,"verification");Directory.CreateDirectory(folder);AssetOperationsTests.Run(folder);}
                    else SelfTest.Run();
                }
                catch(Exception error)
                {
                    string folder=Path.Combine(AppContext.BaseDirectory,"verification");Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder,"self-test-error.txt"),error.ToString());Environment.ExitCode=1;
                }
                finally{host.Close();}
            };
            try{Application.Run(host);}
            catch(Exception error)
            {
                string folder=Path.Combine(AppContext.BaseDirectory,"verification");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"self-test-error.txt"),error.ToString());Environment.ExitCode=1;
            }
            return;
        }
        Application.ThreadException+=(_,e)=>Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>{if(e.ExceptionObject is Exception error)StartupDiagnostics.Log(error);};
        AppConfig configuration;
        try {configuration=AppConfig.Load(Path.Combine(StartupDiagnostics.InstallationRoot,"config.json"));}
        catch(Exception ex)
        {
            Report(ex);return;
        }
        try
        {
            StartupDiagnostics.ValidateSavedWork(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InventoryLabels","settings.json"));
            Application.Run(new MainForm(configuration:configuration));
        }
        catch(Exception ex)
        {
            Report(ex);
        }
    }
    /// <summary>Reports/logs startup or UI failures and offers recovery without automatically deleting user data.</summary>
    static void Report(Exception error)
    {
        string log=StartupDiagnostics.Log(error);
        MessageBox.Show(Ui.T("No se pudo completar la operación. Revisa config.json, el último trabajo y la base de datos. Conserva los archivos afectados antes de restaurar una copia.\n\n","The operation could not complete. Check config.json, last work and the database. Preserve affected files before restoring a backup.\n\n")+error.Message+"\n\n"+log,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Error);
    }
}
