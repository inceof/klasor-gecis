using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using KlasorGecis;

internal static class LocalizationHarness
{
    internal static int Run(string[] args)
    {
        int at = Array.IndexOf(args, "--out");
        string output = Path.GetFullPath(at >= 0 ? args[at + 1] : "localization.json");
        string root = Path.Combine(Path.GetDirectoryName(output)!, "LanguageFixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!ProfilePaths.Select(new(root, true, false), out var profileError)) throw new Exception(profileError);
        var results = new List<object>(); int failures = 0;
        void Check(string name, bool pass) { results.Add(new { Name = name, Pass = pass }); if (!pass) failures++; }
        string settingsPath = Path.Combine(root, "folders.json");
        var seed = new PreferencesStore(settingsPath);
        string path = Directory.CreateDirectory(Path.Combine(root, "Genel Çınar %25")).FullName;
        seed.AddFavorite(path, "Genel", out _);
        try
        {
            using var context = new FolderAppContext(Path.Combine(root, "shortcut.json"), new EmptySource());
            context.ShowSettings(); Pump();
            var field = typeof(FolderAppContext).GetField("_settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var window = (SettingsWindow)field.GetValue(context)!;
            window.TopMost = true;
            // Exercise one real keyboard selection in the owned native combo.
            FocusOwned(window); Pump();
            var first = (ComboBox)window.Controls.Find("AppLanguage", true).Single();
            bool focused = first.Focus(); Pump();
            if (Native.GetForegroundWindow() != window.Handle)
            {
                var point = first.PointToScreen(new Point(first.Width / 2, first.Height / 2));
                nint hit = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
                if ((hit == first.Handle || Native.IsChild(first.Handle, hit)) && Native.ModifiersReleased())
                {
                    GetCursorPos(out var original);
                    SetCursorPos(point.X, point.Y); MouseEvent(2, 0, 0, 0, 0); MouseEvent(4, 0, 0, 0, 0); Pump();
                    GetCursorPos(out var now);
                    if (now.X == point.X && now.Y == point.Y) SetCursorPos(original.X, original.Y);
                }
            }
            nint focus = Native.Focus(Native.GetWindowThreadProcessId(window.Handle, out _));
            bool physical = Native.GetForegroundWindow() == window.Handle && (focus == first.Handle || Native.IsChild(first.Handle, focus));
            results.Add(new { Name = "physical language input observation", Foreground = (long)Native.GetForegroundWindow(),
                Settings = (long)window.Handle, Combo = (long)first.Handle, Focus = (long)focus, FocusClass = Native.Class(focus), Before = context.ReadSettings().Language,
                FocusReturned = focused, first.CanFocus, first.CanSelect, first.IsDisposed, first.Enabled, first.Visible });
            if (physical) physical = Native.Keys((0x28, false), (0x28, true), (0x0D, false), (0x0D, true));
            Pump();
            results.Add(new { Name = "physical language result", Language = context.ReadSettings().Language, InputSent = physical });
            Check("physical Down key selects English and persists it", physical && context.ReadSettings().Language == "en" && new PreferencesStore(settingsPath).Current.Language == "en");
            foreach (var option in Localizer.Languages)
            {
                window.SelectPage("general"); Pump();
                var combo = (ComboBox)window.Controls.Find("AppLanguage", true).Single();
                combo.SelectedItem = option; Pump();
                var state = context.ReadSettings();
                Check(option.Code + " settings and language combo have visible native windows", Native.IsWindowVisible(window.Handle) && Native.IsWindowVisible(window.Controls.Find("AppLanguage", true).Single().Handle));
                Check(option.Code + " language is saved and active immediately", state.Language == option.Code && Localizer.Current == option.Code && new PreferencesStore(settingsPath).Current.Language == option.Code);
                Check(option.Code + " settings native title and selector are translated", window.Text == Localizer.T("Klasör Geçiş — Ayarlar") && window.Controls.Find("AppLanguage", true).OfType<ComboBox>().Single().SelectedItem is LanguageOption selected && selected.Code == option.Code);
                Check(option.Code + " direction is correct", window.RightToLeftLayout == (option.Code == "ar") && window.RightToLeft == (option.Code == "ar" ? RightToLeft.Yes : RightToLeft.No));
                Check(option.Code + " language selection retains preferences and user names", state.Favorites.Single().DisplayName == "Genel" && state.Favorites.Single().Path == path && !state.History && !state.Automatic);
                Check(option.Code + " language field stays inside the scrollable content", window.Controls.Find("AppLanguage", true).Single() is { } visibleCombo && visibleCombo.Visible && window.ClientRectangle.Contains(window.RectangleToClient(visibleCombo.RectangleToScreen(visibleCombo.ClientRectangle))));
                Capture(window, Path.Combine(root, option.Code + "-general.png"));
                window.SelectPage("favorites"); Pump();
                Check(option.Code + " favorite data is not translated", Descendants(window).OfType<Label>().Any(c => c.Text == "Genel") && Descendants(window).OfType<Label>().Any(c => c.Text == path));
                Capture(window, Path.Combine(root, option.Code + "-favorites.png"));
                using var menu = new FolderPickerMenu();
                FolderMenu.Populate(menu, [], new PreferencesStore(settingsPath), null, null, _ => { }, _ => { }, 700);
                menu.Show(window, new Point(25, 70)); Pump();
                var tabs = menu.Items.OfType<ToolStripControlHost>().Select(h => h.Control).OfType<ToolStrip>().Single();
                Check(option.Code + " real popup tabs are translated", tabs.Items["FavoriteFoldersTab"]?.Text == Localizer.T("Favoriler") && menu.Items.Cast<ToolStripItem>().Any(i => i.Tag is FolderRequest request && request.Path == path));
                Capture(menu, Path.Combine(root, option.Code + "-menu.png")); menu.Close();
                using var shortcut = new ShortcutDialog(ShortcutBinding.Default, _ => null);
                shortcut.Show(window); Pump();
                Check(option.Code + " shortcut dialog and key text are correct", shortcut.Text == Localizer.T("Kısayol ayarı") && shortcut.Controls.Find("ShortcutEntry", true).Single().Text == "Ctrl+G");
                Capture(shortcut, Path.Combine(root, option.Code + "-shortcut.png")); shortcut.Close();
            }
            window.SelectPage("general"); Pump();
            var before = File.ReadAllText(settingsPath);
            using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var combo = (ComboBox)window.Controls.Find("AppLanguage", true).Single();
                combo.SelectedItem = Localizer.Languages.Single(l => l.Code == "en"); Pump();
                Check("failed native language change keeps Arabic active and selected", Localizer.Current == "ar" && context.ReadSettings().Language == "ar" && combo.SelectedItem is LanguageOption { Code: "ar" });
                Check("failed language save shows a translated inline error", window.Controls.Find("SettingsError", true).Single() is Label label && label.Visible && label.Text == Localizer.T("Klasör ayarları kaydedilemedi veya dosya dışarıdan değişti; önceki durum korunuyor."));
                Check("failed language save leaves the exact file unchanged", File.ReadAllText(settingsPath) == before);
            }
            window.Close();
            context.ShowSettings(); Pump();
            Check("reopened settings retains saved Arabic", ((SettingsWindow)field.GetValue(context)!).Text == Localizer.T("Klasör Geçiş — Ayarlar") && context.ReadSettings().Language == "ar");
        }
        catch (Exception e) { results.Add(new { Name = "localization harness exception", Pass = false, Error = e.ToString() }); failures++; }
        finally { Localizer.SetCurrent("tr"); }
        File.WriteAllText(output, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow, ProductAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Localizer).Assembly.Location))),
            Failures = failures, Fixture = root, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    private static void Pump()
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 220) { Application.DoEvents(); Thread.Sleep(15); }
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Capture(Control window, string path)
    {
        var bounds = Native.Frame(window.Handle);
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        bitmap.Save(path);
    }
    private static void FocusOwned(Form window)
    {
        uint target = Native.GetWindowThreadProcessId(window.Handle, out uint pid);
        if (pid != Environment.ProcessId) throw new Exception("Not an owned localization window.");
        uint self = GetCurrentThreadId(), foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        bool a = foreground != 0 && foreground != self && AttachThreadInput(self, foreground, true);
        bool b = target != self && target != foreground && AttachThreadInput(self, target, true);
        // The hidden test process may apply SW_HIDE to its first normal Show.
        // Explicitly reveal only this owned native test window before input.
        try { ShowWindow(window.Handle, 5); BringWindowToTop(window.Handle); SetForegroundWindow(window.Handle); }
        finally { if (b) AttachThreadInput(self, target, false); if (a) AttachThreadInput(self, foreground, false); }
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extra);
    private sealed class EmptySource : IExplorerSource
    {
        public Task<IReadOnlyList<FolderLocation>> GetFoldersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<FolderLocation>>([]);
        public Task<IReadOnlyList<FolderLocation>> GetFreshFoldersAsync(CancellationToken token) => GetFoldersAsync(token);
        public void Dispose() { }
    }
}
