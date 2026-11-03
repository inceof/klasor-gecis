using System.Reflection;
using KlasorGecis;

internal static class LocalizationTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        check("legacy preferences retain Turkish as the default language", () =>
            typeof(FolderPreferences).GetProperty("Language")?.GetValue(new FolderPreferences()) as string == "tr");
        check("six-language catalog is present in the application", () =>
            typeof(FolderPreferences).Assembly.GetType("KlasorGecis.Localizer") is not null);
        check("language preference uses the existing settings owner", () =>
            typeof(IAppSettings).GetMethod("SetLanguage") is not null);
        check("all six languages have complete nonempty Unicode catalogs", () =>
            Localizer.Languages.Select(l => l.Code).SequenceEqual(new[] { "tr", "en", "zh-CN", "hi", "es", "ar" }) &&
            Localizer.Catalog.Count >= 150 && Localizer.Catalog.All(entry => entry.Value.Count == 6 &&
                Localizer.Languages.All(l => entry.Value.TryGetValue(l.Code, out var value) && !string.IsNullOrWhiteSpace(value) && !value.Contains('\uFFFD'))));
        check("translated format templates preserve paths and placeholder arguments", () => InEveryLanguage(() =>
        {
            const string path = @"D:\Çınar %25\Genel";
            string message = Localizer.F("{0} klasörüne git: {1}", "Genel", path);
            return message.Contains(path, StringComparison.Ordinal) && message.Contains("Genel", StringComparison.Ordinal) &&
                Localizer.T(path) == path && Localizer.T("unmapped user data") == "unmapped user data";
        }));
        check("old v1 files without Language load as Turkish without a write", () => PreferencesTests.WithFile(path =>
        {
            const string original = "{\"Version\":1,\"Favorites\":[],\"Recent\":[]}";
            File.WriteAllText(path, original);
            var store = new PreferencesStore(path);
            return store.Current.Language == "tr" && store.Error is null && File.ReadAllText(path) == original;
        }));
        check("each language persists without changing favorites and history settings", () => PreferencesTests.WithFile(path =>
        {
            var store = new PreferencesStore(path);
            store.AddFavorite(@"D:\Çınar\Genel", "Genel", out _); store.SetHistory(true, out _);
            foreach (var option in Localizer.Languages)
            {
                if (!store.SetLanguage(option.Code, out var error) || error is not null) return false;
                var loaded = new PreferencesStore(path);
                if (loaded.Current.Language != option.Code || !loaded.Current.HistoryEnabled || loaded.Current.Favorites.Single().DisplayName != "Genel") return false;
            }
            string before = File.ReadAllText(path);
            return !store.SetLanguage("unsupported", out var invalid) && invalid is not null && File.ReadAllText(path) == before;
        }));
        check("locked language preference keeps the stored language and exact file", () => PreferencesTests.WithFile(path =>
        {
            var store = new PreferencesStore(path); store.SetLanguage("en", out _);
            string before = File.ReadAllText(path);
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return !store.SetLanguage("ar", out var error) && error is not null && store.Current.Language == "en" && File.ReadAllText(path) == before;
        }));
        check("unsupported stored languages remain read only instead of being overwritten", () => PreferencesTests.WithFile(path =>
        {
            const string original = "{\"Version\":1,\"Language\":\"unsupported\",\"Favorites\":[],\"Recent\":[]}";
            File.WriteAllText(path, original);
            var store = new PreferencesStore(path);
            return store.Error is not null && !store.SetLanguage("tr", out _) && File.ReadAllText(path) == original;
        }));
        check("only Arabic uses RTL and Hindi selects its script font", () => InEveryLanguage(() =>
            Localizer.IsRtl == (Localizer.Current == "ar") && (Localizer.FontFamily == "Nirmala UI") == (Localizer.Current == "hi")));
        check("native verification can read a log while its owner holds the append handle", () => PreferencesTests.WithFile(path =>
        {
            using var owner = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            byte[] text = System.Text.Encoding.UTF8.GetBytes("navigation ok; trigger=Favorite");
            owner.Write(text); owner.Flush(true);
            return SavedFolderHarness.ReadSharedLog(path) == "navigation ok; trigger=Favorite";
        }));
        check("cached application warnings follow the new language without changing unknown OS errors", () => InEveryLanguage(() =>
            Localizer.Message(Localizer.T("Kısayol ayarı okunamadı; varsayılan kullanılıyor. Mevcut ayar dosyası korunuyor.", "en")) ==
                Localizer.T("Kısayol ayarı okunamadı; varsayılan kullanılıyor. Mevcut ayar dosyası korunuyor.") &&
            Localizer.Message(@"OS error: D:\Genel") == @"OS error: D:\Genel"));
    }

    private static bool InEveryLanguage(Func<bool> action)
    {
        string previous = Localizer.Current;
        try { foreach (var language in Localizer.Languages) { Localizer.SetCurrent(language.Code); if (!action()) return false; } return true; }
        finally { Localizer.SetCurrent(previous); }
    }
}
