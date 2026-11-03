using Microsoft.Win32;
using System.Security.Principal;

namespace KlasorGecis;

// Windows starts this program only when the user explicitly checks the tray option.
// Use a per-user Run entry; no service, task scheduler or UAC bypass.
internal static class StartupSettings
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "KlasorGecis";

    internal static string ExecutablePath =>
        Path.GetFullPath(Environment.ProcessPath ?? Application.ExecutablePath);

    internal static string CommandFor(string path) =>
        "\"" + Path.GetFullPath(path).Replace("\"", "") + "\"";

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        var command = key?.GetValue(ValueName) as string;
        return command is not null && string.Equals(
            command, CommandFor(ExecutablePath), StringComparison.OrdinalIgnoreCase);
    }

    internal static bool SetEnabled(bool enabled, out string error)
    {
        error = "";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null) throw new InvalidOperationException(Localizer.T("Kullanıcı başlangıç anahtarı açılamadı."));
            if (enabled)
            {
                if (!File.Exists(ExecutablePath))
                    throw new FileNotFoundException(Localizer.T("EXE dosyası bulunamadı."), ExecutablePath);
                key.SetValue(ValueName, CommandFor(ExecutablePath), RegistryValueKind.String);
            }
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return IsEnabled() == enabled;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            error = e.Message;
            return false;
        }
    }

    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}