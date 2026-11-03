using System.Text;

namespace KlasorGecis;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (!ProfilePaths.TryParse(args, out var profile, out var profileError) || !ProfilePaths.Select(profile, out profileError))
        {
            MessageBox.Show(profileError, "Klasör Geçiş", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Localizer.SetCurrent(new PreferencesStore(Path.Combine(ProfilePaths.DirectoryPath, "folders.json")).Current.Language);
        using var single = new Mutex(true, @"Local\KlasorGecis.UserSession", out bool first);
        bool ownsMutex = first;
        if (!first && profile.ReplaceInstance)
        {
            try { ownsMutex = single.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { ownsMutex = true; }
        }
        if (!ownsMutex)
        {
            MessageBox.Show(Localizer.T("Klasör Geçiş zaten çalışıyor. Bildirim alanındaki klasör simgesini kullanabilirsiniz."),
                "Klasör Geçiş", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Diagnostics.Write("ui-exception", e.Exception.GetType().Name);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Diagnostics.Write("unhandled", e.ExceptionObject.GetType().Name);
            using var context = new FolderAppContext();
            Application.Run(context);
        }
        finally { single.ReleaseMutex(); }
    }
}

internal static class Diagnostics
{
    private static readonly object Gate = new();
    internal static string DirectoryPath => ProfilePaths.DirectoryPath;
    internal static string LogPath => Path.Combine(DirectoryPath, "activity.log");
    internal static void Write(string action, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024)
                    File.Move(LogPath, LogPath + ".previous", true);
                // No typed filename, clipboard or file contents are logged.
                File.AppendAllText(LogPath, $"{DateTimeOffset.UtcNow:O} {action} {message.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* A failed diagnostic must not block the person's dialog. */ }
    }
}
