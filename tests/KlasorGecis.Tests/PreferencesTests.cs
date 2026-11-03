using KlasorGecis;

internal static class PreferencesTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        check("broken folder preferences cannot be overwritten by a toggle", () => WithFile(path =>
        {
            File.WriteAllText(path, "{broken-json");
            var store = new PreferencesStore(path);
            return !store.SetAutoSwitch(true, out _) && File.ReadAllText(path) == "{broken-json";
        }));
        check("future folder schema cannot be overwritten", () => WithFile(path =>
        {
            File.WriteAllText(path, "{\"Version\":99}");
            var store = new PreferencesStore(path);
            return !store.SetAutoSwitch(true, out _) && File.ReadAllText(path) == "{\"Version\":99}";
        }));
        check("missing preferences default off without creating a file", () => WithFile(path =>
        {
            var store = new PreferencesStore(path);
            return !store.Current.AutoSwitchEnabled && !store.Current.HistoryEnabled && !File.Exists(path);
        }));
        check("favorites survive preference toggles and reload while shortcut stays untouched", () => WithFile(path =>
        {
            string shortcut = Path.Combine(Path.GetDirectoryName(path)!, "shortcut.json");
            File.WriteAllText(shortcut, "{\"Modifiers\":2,\"Key\":75}");
            var store = new PreferencesStore(path);
            return store.AddFavorite(@"D:\Çınar #1 %25", "Çınar", out _) && store.SetAutoSwitch(true, out _) &&
                new PreferencesStore(path).Current.Favorites.Single().Path == @"D:\Çınar #1 %25" &&
                File.ReadAllText(shortcut) == "{\"Modifiers\":2,\"Key\":75}";
        }));
        check("failed replace preserves disk and in-memory preferences", () => WithFile(path =>
        {
            var store = new PreferencesStore(path); store.SetAutoSwitch(true, out _);
            string before = File.ReadAllText(path);
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return !store.SetHistory(true, out _) && !store.Current.HistoryEnabled && File.ReadAllText(path) == before;
        }));
        check("external preferences edit is not overwritten", () => WithFile(path =>
        {
            var store = new PreferencesStore(path); store.SetAutoSwitch(true, out _);
            File.WriteAllText(path, "{\"Version\":1,\"HistoryEnabled\":true}");
            return !store.SetAutoSwitch(false, out _) && File.ReadAllText(path) == "{\"Version\":1,\"HistoryEnabled\":true}";
        }));
        check("external edit during atomic commit is preserved with prior in-memory state", () => WithFile(path =>
        {
            var initial = new PreferencesStore(path); initial.SetAutoSwitch(true, out _);
            const string foreign = "{\"Version\":1,\"HistoryEnabled\":true}";
            var racing = new PreferencesStore(path, () => File.WriteAllText(path, foreign));
            return !racing.SetAutoSwitch(false, out _) && racing.Current.AutoSwitchEnabled &&
                !racing.Current.HistoryEnabled && File.ReadAllText(path) == foreign;
        }));
        check("external creation during first commit cannot be overwritten", () => WithFile(path =>
        {
            const string foreign = "{\"Version\":1,\"HistoryEnabled\":true}";
            var racing = new PreferencesStore(path, () => File.WriteAllText(path, foreign));
            return !racing.SetAutoSwitch(true, out _) && !racing.Current.AutoSwitchEnabled && File.ReadAllText(path) == foreign;
        }));
        check("interrupted commit retains prior disk state and removes only its temporary file", () => WithFile(path =>
        {
            var initial = new PreferencesStore(path); initial.SetAutoSwitch(true, out _);
            string before = File.ReadAllText(path);
            var interrupted = new PreferencesStore(path, () => throw new IOException("Synthetic interrupted commit"));
            return !interrupted.SetHistory(true, out _) && !interrupted.Current.HistoryEnabled && File.ReadAllText(path) == before &&
                Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0;
        }));
        check("history requires consent and never captures pending operations after clear", () => WithFile(path =>
        {
            var store = new PreferencesStore(path);
            store.RecordSuccess(@"D:\Hedef", null, store.HistoryEpoch, out _);
            if (store.Current.Recent.Length != 0) return false;
            store.SetHistory(true, out _); long epoch = store.HistoryEpoch;
            store.RecordSuccess(@"D:\Hedef", null, epoch, out _);
            store.ClearHistory(out _);
            store.RecordSuccess(@"D:\Geç gelen", null, epoch, out _);
            return store.Current.Recent.Length == 0;
        }));
        check("history prioritizes current application, deduplicates and stays bounded", () => WithFile(path =>
        {
            var store = new PreferencesStore(path); store.SetHistory(true, out _);
            long epoch = store.HistoryEpoch;
            for (int i = 0; i < 25; i++) store.RecordSuccess(@"D:\Klasör " + i, @"C:\Other.exe", epoch, out _);
            store.RecordSuccess(@"D:\Özel", @"C:\Target.exe", epoch, out _);
            store.RecordSuccess(@"D:\Yeni", @"C:\Other.exe", epoch, out _);
            return store.Current.Recent.Length == 20 && store.RecentFor(@"C:\Target.exe")[0].Path == @"D:\Özel";
        }));
        check("history disabling retains records but hides them and invalidates old epoch", () => WithFile(path =>
        {
            var store = new PreferencesStore(path); store.SetHistory(true, out _); long epoch = store.HistoryEpoch;
            store.RecordSuccess(@"D:\Bir", null, epoch, out _);
            store.SetHistory(false, out _); store.SetHistory(true, out _);
            store.RecordSuccess(@"D:\Geç", null, epoch, out _);
            return store.Current.Recent.Single().Path == @"D:\Bir";
        }));
        check("profile parser preserves absolute Turkish paths and rejects implicit relative paths", () =>
            ProfilePaths.TryParse(new[] { "--profile-dir", @"D:\Özel Profil" }, out var profile, out _) &&
            profile.Directory == @"D:\Özel Profil" && profile.Explicit &&
            !ProfilePaths.TryParse(new[] { "--profile-dir", "relative" }, out _, out _));
        check("removing one favorite preserves other favorites and preferences", () => WithFile(path =>
        {
            var store = new PreferencesStore(path);
            store.AddFavorite(@"D:\A", "A", out _); store.AddFavorite(@"D:\B", "B", out _);
            store.SetAutoSwitch(true, out _); store.SetHistory(true, out _); store.RemoveFavorite(@"D:\A", out _);
            var loaded = new PreferencesStore(path).Current;
            return loaded.Favorites.Single().Path == @"D:\B" && loaded.AutoSwitchEnabled && loaded.HistoryEnabled;
        }));
        check("favorite paths deduplicate casing and trailing separators including drive roots", () => WithFile(path =>
        {
            var store = new PreferencesStore(path);
            store.AddFavorite(@"D:\Çınar #1 %25\", "Çınar", out _);
            store.AddFavorite(@"d:\çınar #1 %25", "same", out _);
            store.AddFavorite(@"C:\", "Root", out _); store.AddFavorite(@"c:\", "same root", out _);
            return store.Current.Favorites.Length == 2 && store.Current.Favorites[0].DisplayName == "Çınar";
        }));
        check("explicit profile selects only requested Turkish directory and failure never falls back", () => WithFile(path =>
        {
            string previous = ProfilePaths.DirectoryPath;
            try
            {
                string selected = Path.Combine(Path.GetDirectoryName(path)!, "Özel Profil Çınar");
                if (!ProfilePaths.Select(new(selected, true, false), out _) || ProfilePaths.DirectoryPath != selected) return false;
                File.WriteAllText(path, "This is a file, not a writable profile directory.");
                return !ProfilePaths.Select(new(path, true, false), out _) && ProfilePaths.DirectoryPath == selected &&
                    File.ReadAllText(path) == "This is a file, not a writable profile directory.";
            }
            finally { ProfilePaths.Select(new(previous, true, false), out _); }
        }));
        check("concurrent history updates preserve favorites and current consent", () => WithFile(path =>
        {
            var store = new PreferencesStore(path); store.SetHistory(true, out _); long epoch = store.HistoryEpoch;
            bool favorite = false, history = false;
            Parallel.Invoke(() => favorite = store.AddFavorite(@"D:\Kalıcı Çınar", "Çınar", out _),
                () => history = store.RecordSuccess(@"D:\Son Klasör", null, epoch, out _));
            var after = new PreferencesStore(path).Current;
            return favorite && history && after.HistoryEnabled && after.Favorites.Single().Path == @"D:\Kalıcı Çınar" && after.Recent.Single().Path == @"D:\Son Klasör";
        }));
    }

    internal static bool WithFile(Func<string, bool> action)
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "KlasorGecis-preferences-Çınar " + Guid.NewGuid().ToString("N"))).FullName;
        try { return action(Path.Combine(root, "folders.json")); }
        finally { Directory.Delete(root, true); }
    }
}
