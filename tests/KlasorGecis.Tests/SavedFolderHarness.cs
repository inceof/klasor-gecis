using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using KlasorGecis;

internal static class SavedFolderHarness
{
    private sealed class EmptySource : IExplorerSource
    {
        public Task<IReadOnlyList<FolderLocation>> GetFoldersAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<FolderLocation>>([]);
        public Task<IReadOnlyList<FolderLocation>> GetFreshFoldersAsync(CancellationToken token) => throw new Exception("Saved navigation must not query Explorer.");
        public void Dispose() { }
    }
    private static readonly List<object> results = [];
    private static int failures;
    private static Process? product;
    private static uint productThread;
    private static string? executable;
    private static string logPath = "";
    private static string artifactRoot = "";
    private static string artifactPrefix = "";
    private static PointStruct? cursorBefore;
    private static Point lastTestPoint;

    internal static string ReadSharedLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static int Run(string[] args)
    {
        int index = Array.IndexOf(args, "--out");
        string output = Path.GetFullPath(index >= 0 ? args[index + 1] : "saved-results.json");
        artifactRoot = Path.GetDirectoryName(output)!;
        artifactPrefix = Path.GetFileNameWithoutExtension(output);
        string root = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(output)!, "SavedFixtures", Guid.NewGuid().ToString("N"))).FullName;
        if (!ProfilePaths.Select(new(root, true, false), out var profileError)) throw new Exception(profileError);
        string initial = Directory.CreateDirectory(Path.Combine(root, "Başlangıç")).FullName;
        string destination = Directory.CreateDirectory(Path.Combine(root, "Favori Çınar #1 %25")).FullName;
        string file = Path.Combine(root, "Dosyaya dönüşmüş.txt"); File.WriteAllText(file, "synthetic fixture");
        string preferencesPath = Path.Combine(root, "folders.json");
        logPath = Path.Combine(root, "activity.log");
        int appIndex = Array.IndexOf(args, "--app");
        executable = appIndex >= 0 ? Path.GetFullPath(args[appIndex + 1]) : null;
        var preferences = new PreferencesStore(preferencesPath);
        int languageAt = Array.IndexOf(args, "--language");
        if (languageAt >= 0)
        {
            string code = args[languageAt + 1];
            if (!preferences.SetLanguage(code, out var languageError)) throw new Exception(languageError);
            Localizer.SetCurrent(code);
        }
        preferences.AddFavorite(destination, "Çınar favorisi", out _);
        preferences.AddFavorite(file, "Artık klasör değil", out _);
        string longFolder = Directory.CreateDirectory(Path.Combine(root, "Uzun görünen ad")).FullName;
        preferences.AddFavorite(longFolder, string.Concat(Enumerable.Repeat("Çınar 🌳 uzun görünen ad ", 35)), out _);
        preferences.SetHistory(true, out _);
        try
        {
            if (executable is not null)
            {
                if (Process.GetProcessesByName("KlasorGecis").Length != 0) throw new Exception("Existing product instance was left untouched.");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false };
                start.ArgumentList.Add("--profile-dir"); start.ArgumentList.Add(root);
                product = Process.Start(start) ?? throw new Exception("Product did not start.");
            }
            using var context = executable is null ? new FolderAppContext(Path.Combine(root, "shortcut.json"), new EmptySource()) : null;
            RunDialog(context, initial, destination, FolderOrigin.Favorite, false, true);
            RunDialog(context, initial, destination, FolderOrigin.Recent, true, true);
            RunDialog(context, initial, file, FolderOrigin.Favorite, false, false);
            var after = new PreferencesStore(preferencesPath);
            Record("only verified successful folder appears in persisted history", after.Current.Recent.Any(r => r.Path == destination) &&
                !after.Current.Recent.Any(r => r.Path == file));
        }
        catch (Exception e) { results.Add(new { Name = "saved harness", Pass = false, Error = e.ToString() }); failures++; }
        finally
        {
            if (product is not null)
            {
                if (!product.HasExited && productThread != 0) PostThreadMessage(productThread, 0x12, 0, 0);
                if (!product.WaitForExit(2500)) product.Kill();
                product.Dispose();
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow,
            Mode = executable is null ? "in-process empty Explorer source" : "separate portable EXE with isolated profile",
            PortableExeSha256 = executable is null ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))),
            ProductAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ExplorerSource).Assembly.Location))),
            Failures = failures, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    private static void RunDialog(FolderAppContext? context, string initial, string destination, FolderOrigin origin, bool save, bool shouldNavigate)
    {
        using var owner = new Form { Text = "KlasorGecis saved fixture owner", Size = new Size(300, 150) };
        owner.Show();
        using FileDialog dialog = save ? new SaveFileDialog() : new OpenFileDialog();
        dialog.Title = "KlasorGecis saved fixture - " + origin + " - " + save;
        dialog.InitialDirectory = initial; dialog.FileName = "korunacak-Çınar.txt";
        dialog.Filter = "Text|*.txt|All|*.*"; dialog.FilterIndex = 2;
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        bool started = false, completed = false;
        cursorBefore = null;
        var foregroundEvents = new List<object>();
        Native.WinEventProc callback = (_, ev, window, _, _, _, _) =>
        {
            if (ev != 3 || window == 0) return;
            Native.GetWindowThreadProcessId(window, out uint pid);
            foregroundEvents.Add(new { Handle = (long)window, Process = pid, Class = Native.Class(window) });
        };
        nint focusHook = Native.SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
        var readyClock = Stopwatch.StartNew();
        timer.Tick += async (_, _) =>
        {
            if (started || readyClock.ElapsedMilliseconds < 600) return;
            nint window = LiveHarness.FindOwnDialog(dialog.Title);
            if (window == 0) return;
            var detected = DialogTarget.Detect(window);
            if (detected is null && readyClock.ElapsedMilliseconds < 6000) return;
            started = true; timer.Stop();
            try
            {
                LiveHarness.FocusTestWindow(window);
                var target = detected ?? throw new Exception("Target not detected.");
                await Task.Delay(500);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var bar = context is null ? null : (Form)typeof(FolderAppContext).GetField("_bar", flags)!.GetValue(context)!;
                nint barWindow = bar?.Handle ?? 0, arrowWindow = 0;
                var barClock = Stopwatch.StartNew();
                while (barClock.ElapsedMilliseconds < 5000)
                {
                    if (bar is not null) barWindow = bar.Handle;
                    if (bar is null) Native.EnumWindows((h, _) =>
                    {
                        Native.GetWindowThreadProcessId(h, out uint pid);
                        if (pid == product!.Id && Native.IsWindowVisible(h) && Native.Title(h) == "Klasör Geçiş") { barWindow = h; return false; }
                        return true;
                    }, 0);
                    arrowWindow = Native.Children(barWindow).FirstOrDefault(h => Native.Title(h) == "▾" && Native.IsWindowVisible(h) && Native.IsWindowEnabled(h));
                    if (arrowWindow != 0) break;
                    await Task.Delay(50);
                }
                Record(context is null ? "packaged saved menu is available" : "saved menu remains available without any Explorer source", arrowWindow != 0);
                if (arrowWindow == 0)
                {
                    results.Add(new { Name = "row diagnostic", Pass = false, Bar = (long)barWindow,
                        NativeExists = Native.IsWindow(barWindow), NativeVisible = Native.IsWindowVisible(barWindow),
                        ManagedVisible = bar?.Visible, ManagedDisposed = bar?.IsDisposed, TargetCurrent = target.IsCurrent(),
                        ContextTarget = context is null ? null : typeof(FolderAppContext).GetField("_target", flags)!.GetValue(context)?.ToString(),
                        ChildStates = Native.Children(barWindow).Select(h => new { Handle = (long)h, Class = Native.Class(h), Visible = Native.IsWindowVisible(h), Enabled = Native.IsWindowEnabled(h) }).ToArray() });
                    throw new Exception("Menu arrow did not appear.");
                }
                productThread = Native.GetWindowThreadProcessId(barWindow, out _);
                Native.TryReadText(target.FilenameEdit, out string beforeName); int? beforeFilter = target.FilterIndex();
                nint beforeMenuFocus = Native.Focus(target.ThreadId);
                int logStart = File.Exists(logPath) ? ReadSharedLog(logPath).Length : 0;
                var arrowBounds = Native.Frame(arrowWindow);
                if (!Click(target, arrowWindow, Center(arrowBounds))) throw new Exception("Menu arrow could not be clicked.");
                await Task.Delay(120);
                results.Add(new { Name = "menu focus observation", TargetCurrent = target.IsCurrent(),
                    ForegroundClass = Native.Class(Native.GetForegroundWindow()), Foreground = (long)Native.GetForegroundWindow(),
                    Target = (long)target.Handle, InputFocus = (long)Native.Focus(target.ThreadId), BeforeFocus = (long)beforeMenuFocus });
                nint menuWindow; Point point;
                if (context is not null)
                {
                    var menu = (ContextMenuStrip)typeof(FolderAppContext).GetField("_foldersMenu", flags)!.GetValue(context)!;
                    var tabs = menu.Items.OfType<ToolStripControlHost>().Select(h => h.Control).OfType<ToolStrip>().Single();
                    var tab = tabs.Items[origin == FolderOrigin.Favorite ? "FavoriteFoldersTab" : "RecentFoldersTab"]!;
                    if (!Click(target, menu.Handle, tabs.PointToScreen(Center(tab.Bounds)))) throw new Exception("Folder tab could not be clicked.");
                    await Task.Delay(100);
                    Record("folder tab preserves dialog foreground and existing input focus", target.IsCurrent() && Native.Focus(target.ThreadId) == beforeMenuFocus && menu.Visible);
                    Record("folder category strip spans the popup without a wide empty trailing area", menu.DisplayRectangle.Width - tabs.Width <= 24 * menu.DeviceDpi / 96);
                    results.Add(new { Name = "popup geometry", Client = menu.ClientSize, Display = menu.DisplayRectangle,
                        Tabs = tabs.Size, Maximum = menu.MaximumSize,
                        Items = menu.Items.Cast<ToolStripItem>().Where(i => i.Available).Select(i => new { Kind = i.GetType().Name, i.Size,
                            Preferred = i.GetPreferredSize(Size.Empty), TextWidth = TextRenderer.MeasureText(i.Text, menu.Font).Width }).ToArray() });
                    var arrows = new List<(Rectangle Arrow, Size Item)>();
                    ToolStripArrowRenderEventHandler observeArrow = (_, e) => { if (e.Item is { } row) arrows.Add((e.ArrowRectangle, row.Size)); };
                    menu.Renderer.RenderArrow += observeArrow;
                    try { menu.Invalidate(true); menu.Update(); }
                    finally { menu.Renderer.RenderArrow -= observeArrow; }
                    Record("submenu arrow is painted inside the visible footer row", arrows.Count > 0 &&
                        arrows.All(a => new Rectangle(Point.Empty, a.Item).Contains(a.Arrow)));
                    results.Add(new { Name = "submenu arrow geometry", Arrows = arrows.Select(a => new { a.Arrow, a.Item }).ToArray() });
                    if (origin == FolderOrigin.Favorite && shouldNavigate)
                    {
                        if (!target.IsCurrent() || !Native.ModifiersReleased()) throw new Exception("Keyboard tab guard changed.");
                        Native.Keys((0x11, false), (0x09, false), (0x09, true), (0x11, true)); await Task.Delay(100);
                        Record("Ctrl Tab selects recent folders without changing native dialog focus", tabs.Items["RecentFoldersTab"] is ToolStripButton { Checked: true } && target.IsCurrent() && Native.Focus(target.ThreadId) == beforeMenuFocus);
                        Native.Keys((0x11, false), (0x10, false), (0x09, false), (0x09, true), (0x10, true), (0x11, true)); await Task.Delay(100);
                        Record("Ctrl Shift Tab returns to favorites without navigating", tabs.Items["FavoriteFoldersTab"] is ToolStripButton { Checked: true } && target.IsCurrent() && target.CurrentFolder() == initial);
                    }
                    var item = menu.Items.Cast<ToolStripItem>().Single(i => i.Tag is FolderRequest r && r.Origin == origin && r.Path == destination);
                    var longItem = menu.Items.Cast<ToolStripItem>().Single(i => i.Tag is FolderRequest r && r.Path.EndsWith("Uzun görünen ad", StringComparison.Ordinal));
                    var work = Screen.FromControl(menu).WorkingArea;
                    Record("long favorite caption stays within work area and retains full accessible name",
                        work.Contains(menu.Bounds) && longItem.Text is { } caption && caption.EndsWith('…') &&
                        longItem.AccessibleName is { } accessibleName && accessibleName.Length > caption.Length &&
                        longItem.ToolTipText?.EndsWith("Uzun görünen ad", StringComparison.Ordinal) == true);
                    var captureBounds = menu.Bounds;
                    using (var bitmap = new Bitmap(captureBounds.Width, captureBounds.Height))
                    {
                        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(captureBounds.Location, Point.Empty, captureBounds.Size);
                        bitmap.Save(Path.Combine(Path.GetDirectoryName(logPath)!, origin + "-menu.png"));
                    }
                    menuWindow = menu.Handle;
                    point = menu.PointToScreen(Center(item.Bounds));
                }
                else
                {
                    var tab = FindAccessibleName(name => name == Localizer.T(origin == FolderOrigin.Favorite ? "Favoriler" : "Son klasörler"));
                    if (!Click(target, tab.Window, tab.Point)) throw new Exception("Packaged folder tab could not be clicked.");
                    await Task.Delay(100);
                    Record("packaged folder tab preserves dialog foreground and existing input focus", target.IsCurrent() && Native.Focus(target.ThreadId) == beforeMenuFocus);
                    (menuWindow, point) = FindAccessibleName(name => name.Contains(destination, StringComparison.OrdinalIgnoreCase));
                }
                if (!Click(target, menuWindow, point)) throw new Exception("Saved menu item could not be clicked.");
                await Task.Delay(200);
                var watch = Stopwatch.StartNew();
                if (context is not null)
                    while (watch.ElapsedMilliseconds < 5000 && (bool)typeof(FolderAppContext).GetField("_busy", flags)!.GetValue(context)!) await Task.Delay(30);
                else
                    while (watch.ElapsedMilliseconds < 5000 && (shouldNavigate
                        ? target.CurrentFolder() != destination || !ReadSharedLog(logPath)[logStart..].Contains("navigation ok; trigger=" + origin)
                        : !RejectedSince(logStart))) await Task.Delay(30);
                Native.TryReadText(target.FilenameEdit, out string afterName);
                bool folder = string.Equals(target.CurrentFolder(), shouldNavigate ? destination : initial, StringComparison.OrdinalIgnoreCase);
                Record(origin + (shouldNavigate ? " navigates in same dialog and preserves state" : " rejects file without navigating"),
                    target.IsCurrent() && folder && beforeName == afterName && beforeFilter == target.FilterIndex() &&
                    (context is not null || (shouldNavigate ? ReadSharedLog(logPath)[logStart..].Contains("navigation ok; trigger=" + origin) : RejectedSince(logStart))));
                if (!shouldNavigate)
                {
                    var row = Native.Frame(barWindow);
                    using var bitmap = new Bitmap(row.Width, row.Height);
                    using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(row.Location, Point.Empty, row.Size);
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(logPath)!, "Error-row.png"));
                }
                results.Add(new { Name = "saved interaction state", ActualFolder = target.CurrentFolder(), Expected = shouldNavigate ? destination : initial,
                    Current = target.IsCurrent(), FilenameSame = beforeName == afterName, FilterSame = beforeFilter == target.FilterIndex(),
                    Target = (long)target.Handle, Bar = (long)barWindow, ForegroundEvents = foregroundEvents.ToArray(),
                    Log = context is null ? ReadSharedLog(logPath) : null });
                completed = true;
                if (context is null && !shouldNavigate)
                {
                    if (!Click(target, arrowWindow, Center(Native.Frame(arrowWindow)))) throw new Exception("Settings menu could not be reopened.");
                    await Task.Delay(120);
                    var settingsItem = FindAccessibleName(name => name == Localizer.T("Ayarlar…"));
                    if (!Click(target, settingsItem.Window, settingsItem.Point)) throw new Exception("Packaged settings entry could not be clicked.");
                    nint settingsWindow = 0;
                    var settingsClock = Stopwatch.StartNew();
                    while (settingsClock.ElapsedMilliseconds < 4000 && settingsWindow == 0)
                    {
                        Native.EnumWindows((candidate, _) =>
                        {
                            Native.GetWindowThreadProcessId(candidate, out uint pid);
                            if (pid == product!.Id && Native.IsWindowVisible(candidate) && Native.Title(candidate) == Localizer.T("Klasör Geçiş — Ayarlar"))
                            { settingsWindow = candidate; return false; }
                            return true;
                        }, 0);
                        if (settingsWindow == 0) await Task.Delay(50);
                    }
                    nint firstVisibleForeground = Native.GetForegroundWindow();
                    // ShowWindow can expose WS_VISIBLE before WinForms finishes activation.
                    // Wait for the required state; never activate the product from the test.
                    while (settingsWindow != 0 && Native.GetForegroundWindow() != settingsWindow && settingsClock.ElapsedMilliseconds < 4000)
                        await Task.Delay(30);
                    long activationElapsedMs = settingsClock.ElapsedMilliseconds;
                    Record("packaged popup opens the real unified settings window", settingsWindow != 0 && Native.GetForegroundWindow() == settingsWindow);
                    if (settingsWindow != 0)
                    {
                        bool controlsReady = false;
                        settingsClock.Restart();
                        while (settingsClock.ElapsedMilliseconds < 4000)
                        {
                            controlsReady = Native.Children(settingsWindow).Any(child => Native.IsWindowVisible(child) && Native.Title(child) == Localizer.T("Kısayolu değiştir"));
                            if (controlsReady) break;
                            await Task.Delay(30);
                        }
                        Record("packaged settings exposes its actual native controls", controlsReady);
                        results.Add(new { Name = "settings opening observation", FirstVisibleForeground = (long)firstVisibleForeground,
                            SettingsWindow = (long)settingsWindow, ControlsReadyForeground = (long)Native.GetForegroundWindow(),
                            ActivationElapsedMs = activationElapsedMs, ControlsReadyElapsedMs = settingsClock.ElapsedMilliseconds });
                        // Allow the first WM_PAINT/compositor frame after native child creation.
                        await Task.Delay(250);
                        var bounds = Native.Frame(settingsWindow);
                        using var image = new Bitmap(bounds.Width, bounds.Height);
                        using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                        image.Save(Path.Combine(Path.GetDirectoryName(logPath)!, "Settings-exe.png"));
                        image.Save(Path.Combine(artifactRoot, artifactPrefix + "-settings.png"));
                        PostMessage(settingsWindow, 0x10, 0, 0);
                        await Task.Delay(150);
                    }
                }
            }
            catch (Exception e) { results.Add(new { Name = "saved interaction", Pass = false, Error = e.ToString() }); failures++; }
            finally { if (Native.IsWindow(window)) PostMessage(window, 0x10, 0, 0); }
        };
        timer.Start(); var result = dialog.ShowDialog(owner); timer.Stop();
        if (focusHook != 0) Native.UnhookWinEvent(focusHook);
        GC.KeepAlive(callback);
        if (cursorBefore is { } original)
        {
            GetCursorPos(out var current);
            if (current.X == lastTestPoint.X && current.Y == lastTestPoint.Y) SetCursorPos(original.X, original.Y);
        }
        Record("controlled dialog is canceled without choosing or saving a file", completed && result == DialogResult.Cancel);
    }

    private static Point Center(Rectangle bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

    private static bool Click(DialogTarget target, nint owner, Point point)
    {
        var hit = WindowFromPoint(new PointStruct { X = point.X, Y = point.Y });
        if (!target.IsCurrent() || (hit != owner && !Native.IsChild(owner, hit))) return false;
        GetCursorPos(out var before); cursorBefore ??= before;
        if (!SetCursorPos(point.X, point.Y)) return false;
        lastTestPoint = point;
        MouseEvent(2, 0, 0, 0, 0); MouseEvent(4, 0, 0, 0, 0); return true;
    }

    private static (nint Window, Point Point) FindAccessibleName(Func<string, bool> matches)
    {
        (nint Window, Point Point) found = default;
        var observations = new List<object>();
        Native.EnumWindows((window, _) =>
        {
            Native.GetWindowThreadProcessId(window, out uint pid);
            if (pid != product!.Id || !Native.IsWindowVisible(window) || !Native.Class(window).Contains("WindowsForms")) return true;
            var accessible = Accessible(window);
            if (accessible is null) return true;
            try
            {
                int role = Convert.ToInt32(accessible.get_accRole(0));
                observations.Add(new { Window = (long)window, Role = role, Count = accessible.accChildCount, Class = Native.Class(window) });
                if (role != 11) return true;
                bool Search(Accessibility.IAccessible node, int depth)
                {
                    if (depth > 8) return false;
                    for (int index = 1; index <= Math.Min(node.accChildCount, 256); index++)
                    {
                        var child = node.get_accChild(index) as Accessibility.IAccessible;
                        try
                        {
                            var entry = child ?? node; object id = child is null ? index : 0;
                            string name = entry.get_accName(id) ?? "";
                            int state = Convert.ToInt32(entry.get_accState(id));
                            observations.Add(new { Depth = depth, Caption = name.Contains('\\') || name.Contains(':') ? "[path omitted]" : name, State = state, Role = entry.get_accRole(id) });
                            if ((state & 0x18000) != 0) continue;
                            if (matches(name))
                            {
                                entry.accLocation(out int left, out int top, out int width, out int height, id);
                                if (width > 0 && height > 0) { found = (window, new Point(left + width / 2, top + height / 2)); return true; }
                            }
                            if (child is not null && !ReferenceEquals(child, node) && Search(child, depth + 1)) return true;
                        }
                        finally { if (child is not null && !ReferenceEquals(child, node)) Marshal.ReleaseComObject(child); }
                    }
                    return false;
                }
                if (Search(accessible, 0)) return false;
                // A hosted WinForms ToolStrip exposes its native child HWND separately
                // from the popup's simple MSAA ControlHost entry.
                foreach (nint childWindow in Native.Children(window).Where(Native.IsWindowVisible))
                {
                    var childAccessible = Accessible(childWindow);
                    if (childAccessible is null) continue;
                    try { if (Search(childAccessible, 0)) return false; }
                    finally { Marshal.ReleaseComObject(childAccessible); }
                }
            }
            finally { Marshal.ReleaseComObject(accessible); }
            return true;
        }, 0);
        if (found.Window == 0)
        {
            results.Add(new { Name = "native accessibility observations", Nodes = observations });
            throw new Exception("Saved item could not be found in native accessibility tree.");
        }
        return found;
    }

    private static bool RejectedSince(int start)
    {
        return File.Exists(logPath) && ReadSharedLog(logPath)[start..].Contains("folder-unavailable origin=Favorite; status=NotDirectory", StringComparison.Ordinal);
    }

    private static Accessibility.IAccessible? Accessible(nint window)
    {
        var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        return AccessibleObjectFromWindow(window, 0xFFFFFFFC, ref iid, out var value) == 0 ? value as Accessibility.IAccessible : null;
    }

    private static void Record(string name, bool pass) { results.Add(new { Name = name, Pass = pass }); if (!pass) failures++; Console.WriteLine((pass ? "PASS " : "FAIL ") + name); }
    [StructLayout(LayoutKind.Sequential)] private struct PointStruct { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(PointStruct point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointStruct point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nint w, nint l);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(nint window, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object value);
}
