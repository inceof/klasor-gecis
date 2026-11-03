using System.Reflection;
using KlasorGecis;

internal static class NativeUiTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        check("successful startup persistence produces no error message", () =>
        {
            bool current = false;
            var action = new StartupSettingAction(() => current, value => { current = value; return (true, ""); });
            return action.Apply(true) is null && current;
        });
        check("failed startup verification produces an actionable nonempty error", () =>
        {
            var action = new StartupSettingAction(() => false, _ => (false, ""));
            return !string.IsNullOrWhiteSpace(action.Apply(true));
        });
        check("folder tabs change the visible origin without navigating", () => InSta(() => PreferencesTests.WithFile(path =>
        {
            var preferences = new PreferencesStore(path);
            preferences.AddFavorite(@"D:\Favori Çınar", "Çınar", out _);
            using var menu = new ContextMenuStrip();
            FolderRequest? selected = null;
            FolderMenu.Populate(menu, [new(11, 22, @"D:\Kaynak", "Kaynak")], preferences, null, null,
                request => selected = request, _ => { }, 420);
            var tabs = menu.Items.OfType<ToolStripControlHost>().Select(h => h.Control).OfType<ToolStrip>().SingleOrDefault();
            if (tabs?.Items["FavoriteFoldersTab"] is not ToolStripButton favoriteTab) return false;
            favoriteTab.PerformClick();
            var choices = menu.Items.Cast<ToolStripItem>().Where(i => i.Available && i.Tag is FolderRequest).ToArray();
            if (selected is not null || choices.Length != 1 || choices[0].Tag is not FolderRequest { Origin: FolderOrigin.Favorite }) return false;
            choices[0].PerformClick();
            return selected is { Origin: FolderOrigin.Favorite, ExplorerSource: null, Path: @"D:\Favori Çınar" };
        })));
        check("saved folder tab is initially usable with no open Explorer", () => InSta(() => PreferencesTests.WithFile(path =>
        {
            var preferences = new PreferencesStore(path);
            preferences.AddFavorite(@"D:\Favori", "Favori", out _);
            using var menu = new ContextMenuStrip();
            FolderMenu.Populate(menu, [], preferences, null, null, _ => { }, _ => { }, 300);
            var tabs = menu.Items.OfType<ToolStripControlHost>().Select(h => h.Control).OfType<ToolStrip>().SingleOrDefault();
            return tabs?.Items["FavoriteFoldersTab"] is ToolStripButton { Checked: true } &&
                menu.Items.Cast<ToolStripItem>().Any(i => i.Available && i.Tag is FolderRequest { Origin: FolderOrigin.Favorite });
        })));
        check("Ctrl Tab cycles folder origins without invoking a navigation request", () => InSta(() => PreferencesTests.WithFile(path =>
        {
            var preferences = new PreferencesStore(path);
            preferences.AddFavorite(@"D:\Favori", "Favori", out _);
            using var menu = new FolderPickerMenu();
            int requests = 0;
            FolderMenu.Populate(menu, [new(11, 22, @"D:\Kaynak", "Kaynak")], preferences, null, null, _ => requests++, _ => { }, 420);
            var method = typeof(FolderPickerMenu).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool handled = (bool)method.Invoke(menu, [Keys.Control | Keys.Tab])!;
            var tabs = menu.Items.OfType<ToolStripControlHost>().Select(h => h.Control).OfType<ToolStrip>().Single();
            return handled && requests == 0 && tabs.Items["FavoriteFoldersTab"] is ToolStripButton { Checked: true };
        })));
        check("tray exposes the unified settings entry without writing preferences", () => InSta(() => PreferencesTests.WithFile(path =>
        {
            using var context = new FolderAppContext(Path.Combine(Path.GetDirectoryName(path)!, "shortcut.json"), new EmptySource());
            var tray = (NotifyIcon)typeof(FolderAppContext).GetField("_tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
            return tray.ContextMenuStrip?.Items["AppSettings"] is ToolStripMenuItem && !File.Exists(path);
        })));
        check("settings write failure retains the stored value and switch while showing the error", () => InSta(() => PreferencesTests.WithFile(path =>
        {
            using var context = new FolderAppContext(Path.Combine(Path.GetDirectoryName(path)!, "shortcut.json"), new EmptySource());
            if (context.SetAutomatic(true) is not null) return false;
            string before = File.ReadAllText(path);
            using var window = new SettingsWindow(context);
            var toggle = (CheckBox)window.Controls.Find("Setting_automatic", true).Single();
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toggle, [EventArgs.Empty]);
            var warning = window.Controls.Find("SettingsError", true).OfType<Label>().Single();
            return toggle.Checked && context.ReadSettings().Automatic && warning.Text.Length > 0 && File.ReadAllText(path) == before;
        })));
    }

    private sealed class EmptySource : IExplorerSource
    {
        public Task<IReadOnlyList<FolderLocation>> GetFoldersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<FolderLocation>>([]);
        public Task<IReadOnlyList<FolderLocation>> GetFreshFoldersAsync(CancellationToken token) => GetFoldersAsync(token);
        public void Dispose() { }
    }
    private static bool InSta(Func<bool> action)
    {
        bool result = false; Exception? error = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) throw error;
        return result;
    }
}
