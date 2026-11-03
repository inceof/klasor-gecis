using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using KlasorGecis;

internal static class NativeSettingsHarness
{
    private sealed class EmptySource : IExplorerSource
    {
        public Task<IReadOnlyList<FolderLocation>> GetFoldersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<FolderLocation>>([]);
        public Task<IReadOnlyList<FolderLocation>> GetFreshFoldersAsync(CancellationToken token) => GetFoldersAsync(token);
        public void Dispose() { }
    }
    internal static int Run(string[] args)
    {
        string output = Path.GetFullPath(args[Array.IndexOf(args, "--out") + 1]);
        string root = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(output)!, "NativeUiFixtures", Guid.NewGuid().ToString("N"))).FullName;
        if (!ProfilePaths.Select(new(root, true, false), out var profileError)) throw new Exception(profileError);
        string path = Path.Combine(root, "folders.json");
        var seed = new PreferencesStore(path);
        string destination = Directory.CreateDirectory(Path.Combine(root, "Favori Çınar #1 %25")).FullName;
        seed.AddFavorite(destination, "Çınar favorisi", out _);
        seed.AddFavorite(root, string.Concat(Enumerable.Repeat("Uzun Türkçe görünen ad Çınar 🌳 ", 20)), out _);
        byte[] initial = File.ReadAllBytes(path);
        var results = new List<object>(); int failures = 0;
        void Check(string name, bool pass) { results.Add(new { Name = name, Pass = pass }); if (!pass) failures++; }
        void Pump() { Application.DoEvents(); }
        T Find<T>(Control owner, string name) where T : Control => owner.Controls.Find(name, true).OfType<T>().Single();
        void Picture(Form window, string name)
        {
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-" + name));
        }
        using var context = new FolderAppContext(Path.Combine(root, "shortcut.json"), new EmptySource());
        try
        {
            context.ShowSettings(); Pump();
            var window = Application.OpenForms.OfType<SettingsWindow>().Single();
            LiveHarness.FocusTestWindow(window.Handle); Pump();
            Check("opening unified settings does not write preferences or enable optional behavior", initial.SequenceEqual(File.ReadAllBytes(path)) &&
                !context.ReadSettings().Automatic && !context.ReadSettings().History);
            Picture(window, "feature-native-ui-settings.png");
            var automatic = Find<CheckBox>(window, "Setting_automatic");
            automatic.AccessibilityObject.DoDefaultAction(); Pump();
            Check("automatic toggle persists through the existing preferences owner", automatic.Checked && new PreferencesStore(path).Current.AutoSwitchEnabled);
            byte[] beforeFailure = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                automatic.AccessibilityObject.DoDefaultAction(); Pump();
                var warning = Find<Label>(window, "SettingsError");
                Check("failed write keeps the old switch, memory and disk and shows an inline error", automatic.Checked && context.ReadSettings().Automatic &&
                    warning.Visible && warning.Text.Length > 0 && beforeFailure.SequenceEqual(File.ReadAllBytes(path)));
                Picture(window, "feature-native-ui-settings-error.png");
            }
            automatic.AccessibilityObject.DoDefaultAction(); Pump();
            Check("successful retry clears the error and turns automatic behavior off", !automatic.Checked && !new PreferencesStore(path).Current.AutoSwitchEnabled && !Find<Label>(window, "SettingsError").Visible);
            window.SelectPage("shortcut"); Pump();
            var changed = ShortcutBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F22)!;
            string? shortcutError = context.ApplyShortcut(changed); window.RefreshState(); Pump();
            Check("shortcut page displays the real registered and persisted setting", shortcutError is null && Find<Label>(window, "CurrentShortcut").Text == "Ctrl+Alt+Shift+F22" && ShortcutSettings.Load(Path.Combine(root, "shortcut.json"), out _) == changed);
            context.ShowSettings("favorites"); Pump();
            Check("all settings sections reuse a single application window", Application.OpenForms.OfType<SettingsWindow>().Count() == 1 && ReferenceEquals(window, Application.OpenForms.OfType<SettingsWindow>().Single()));
            window.WindowState = FormWindowState.Minimized; context.ShowSettings("favorites"); Pump();
            Check("opening settings restores the existing minimized window", window.WindowState != FormWindowState.Minimized);
            window.WindowState = FormWindowState.Normal; Pump();
            Check("adding current favorite is disabled without a live Explorer source", !Find<Button>(window, "AddCurrentFavorite").Enabled);
            window.Size = window.MinimumSize; Pump();
            var viewport = Find<Panel>(window, "SettingsContent");
            Check("long Turkish favorite names wrap within the minimum window width", !viewport.HorizontalScroll.Visible && viewport.DisplayRectangle.Width <= viewport.ClientSize.Width);
            IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
            var longRow = Descendants(window).OfType<TableLayoutPanel>().Single(c => c.AccessibleName?.StartsWith("Uzun Türkçe", StringComparison.Ordinal) == true);
            Check("long favorite rows stay compact and retain their full accessible name", longRow.Height <= 130 * window.DeviceDpi / 96 && longRow.AccessibleName!.Length > 300);
            Picture(window, "feature-native-ui-favorites.png");
            var remove = window.Controls.Find("RemoveFavorite", true).OfType<Button>().First(); remove.PerformClick(); Pump();
            Check("favorite removal updates the real store and keeps other favorites", new PreferencesStore(path).Current.Favorites.Length == 1 && context.ReadSettings().Favorites.Length == 1);
            window.SelectPage("privacy"); Pump();
            var history = Find<CheckBox>(window, "Setting_history");
            Check("history starts off and an empty history cannot be cleared", !history.Checked && !Find<Button>(window, "ClearHistory").Enabled);
            history.AccessibilityObject.DoDefaultAction(); Pump();
            Check("history toggle uses existing consent persistence", history.Checked && new PreferencesStore(path).Current.HistoryEnabled);
            var store = (PreferencesStore)typeof(FolderAppContext).GetField("_preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
            store.RecordSuccess(destination, null, store.HistoryEpoch, out _); window.RefreshState(); Pump();
            history.AccessibilityObject.DoDefaultAction(); Pump();
            Check("disabling history retains old entries until explicitly cleared", !history.Checked && new PreferencesStore(path).Current.Recent.Length == 1 && Find<Button>(window, "ClearHistory").Enabled);
            Find<Button>(window, "ClearHistory").PerformClick(); Pump();
            Check("clear history removes persisted records and disables the empty action", new PreferencesStore(path).Current.Recent.Length == 0 && !Find<Button>(window, "ClearHistory").Enabled);
            window.Close(); Pump(); context.ShowSettings("privacy"); Pump();
            var reopened = Application.OpenForms.OfType<SettingsWindow>().Single();
            Check("reopening settings reloads the current state without creating a duplicate", !ReferenceEquals(reopened, window) && !Find<CheckBox>(reopened, "Setting_history").Checked && context.ReadSettings().Favorites.Length == 1);
            reopened.Close(); Pump();
        }
        catch (Exception exception) { results.Add(new { Name = "native settings harness", Pass = false, Error = exception.ToString() }); failures++; }
        File.WriteAllText(output, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow, Mode = "native WinForms in-process with isolated real preferences store; startup is read only",
            ProductAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(FolderAppContext).Assembly.Location))),
            Failures = failures, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
