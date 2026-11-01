namespace TagPrinter;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        AppConfig configuration;
        try {configuration=AppConfig.Load(Path.Combine(AppContext.BaseDirectory,"config.json"));}
        catch(Exception ex) when(ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show("Could not load config.json. Correct the file and restart.\n\n"+ex.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Error);return;
        }
        try {Application.Run(new MainForm(configuration:configuration));}
        catch(Exception ex) when(ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidDataException)
        {
            MessageBox.Show("Could not open TagPrinter data. Check the data folder or restore a backup.\n\n"+ex.Message,"TagPrinter",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}
