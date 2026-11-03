namespace KlasorGecis;

internal sealed record AppSettingsState(bool Enabled, bool Automatic, bool History, bool Startup,
    bool AutomaticAvailable, ShortcutBinding Shortcut, FavoriteFolder[] Favorites, int RecentCount,
    FolderLocation? CurrentFolder, string? Warning, string Language = "tr");

/// <summary>The existing application owns settings, persistence, and shortcut registration.</summary>
internal interface IAppSettings
{
    AppSettingsState ReadSettings();
    string? SetEnabled(bool value);
    string? SetAutomatic(bool value);
    string? SetHistory(bool value);
    string? SetStartup(bool value);
    string? SetShortcut(ShortcutBinding value);
    string? ClearHistory();
    string? AddCurrentFavorite();
    string? RemoveFavorite(string path);
    string? SetLanguage(string code);
}

/// <summary>Isolates the existing startup writer's bool/error contract from UI feedback.</summary>
internal sealed class StartupSettingAction(Func<bool> read, Func<bool, (bool Saved, string? Error)> write)
{
    internal string? Apply(bool value)
    {
        if (read() == value) return null;
        var result = write(value);
        return result.Saved ? null : string.IsNullOrWhiteSpace(result.Error)
            ? Localizer.T("Windows başlangıç ayarı kaydedilemedi; önceki durumu kontrol edin.") : result.Error;
    }
}
