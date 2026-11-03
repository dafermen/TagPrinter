namespace TagPrinter;

/// <summary>Validates mutable user state and records startup failures without overwriting damaged data.</summary>
internal static class StartupDiagnostics
{
    /// <summary>Portable media/config live above app/; development builds keep config beside the executable.</summary>
    internal static string InstallationRoot => string.Equals(new DirectoryInfo(AppContext.BaseDirectory).Name,"app",StringComparison.OrdinalIgnoreCase)
        ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..")) : AppContext.BaseDirectory;

    /// <summary>Rejects malformed saved work before a form/autosave can replace it with defaults.</summary>
    internal static void ValidateSavedWork(string path)
    {
        if(File.Exists(path))_ = WorkStore.Load(path);
    }

    /// <summary>Writes a timestamped error in user data; logging failure must not hide the original error.</summary>
    internal static string Log(Exception error)
    {
        try
        {
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TagPrinter","logs");Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,$"startup-{DateTime.UtcNow:yyyyMMdd}.log");
            File.AppendAllText(path,DateTimeOffset.UtcNow.ToString("O")+Environment.NewLine+error+Environment.NewLine);
            return path;
        }
        catch{return "Log unavailable; preserve the error message.";}
    }
}
