using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using KlasorGecis;

/// <summary>Real-process, real-input smoke test; touches only uniquely named test fixtures.</summary>
internal static class AppHarness
{
    private static readonly List<object> Results = [];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static int _failures, _blocked;
    private static Process? _product;
    private static int _productId;
    private static FolderAppContext? _context;
    private static bool _features;
    private static bool _automatic, _automaticOff, _automaticNoop, _automaticInterrupted;
    private static ShortcutBinding _binding = ShortcutBinding.Default;
    private static uint _productThread;
    private static nint _sourceWindow;
    private static string _sourcePath = "", _output = "";
    private static bool _ownsSource;
    private static nint _otherWindow;
    private static string _otherPath = "", _fixtureRoot = "";
    private static string? _testedExe;
    private static string _productLogPath = Diagnostics.LogPath;
    private static object[] _lastExplorerProbe = [];

    internal static int Run(string[] args)
    {
        // LiveHarness has already configured this STA's WinForms DPI and styles.
        _output = Path.GetFullPath(Option(args, "--out") ?? "app-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        var keep = args.Contains("--keep-app");
        _features = args[0] == "--feature-suite";
        _automatic = args[0] is "--automatic-suite" or "--automatic-off-suite" or "--automatic-noop-suite" or "--automatic-interrupted-suite";
        _automaticOff = args[0] == "--automatic-off-suite";
        _automaticNoop = args[0] == "--automatic-noop-suite";
        _automaticInterrupted = args[0] == "--automatic-interrupted-suite";
        try
        {
            string exe = Option(args, "--app") ?? Option(args, "--exe") ?? FindProductExe();
            _testedExe = _features ? null : exe;
            var running = Process.GetProcessesByName("KlasorGecis");
            try
            {
                if (running.Length != 0)
                {
                    Record("isolated product launch", "BLOCKED", new { Reason = "An existing product instance is running; it was left untouched.", Pids = running.Select(p => p.Id).ToArray() });
                    return Finish();
                }
            }
            finally { foreach (var p in running) p.Dispose(); }
            string token = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
            string fixtureRoot = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(_output)!, "AppFixtures", token)).FullName;
            _fixtureRoot = fixtureRoot;
            string initial = Directory.CreateDirectory(Path.Combine(fixtureRoot, "Initial")).FullName;
            _sourcePath = Directory.CreateDirectory(Path.Combine(fixtureRoot, "Kaynak Çınar #1 %25")).FullName;
            var before = ExplorerWindows().Select(x => x.Handle).ToHashSet();
            OpenExplorer(_sourcePath);
            PumpUntil(() =>
            {
                var found = ExplorerWindows().FirstOrDefault(x => SamePath(x.Path, _sourcePath));
                _sourceWindow = found?.Handle ?? 0;
                return _sourceWindow != 0;
            }, 6000);
            _ownsSource = _sourceWindow != 0 && !before.Contains(_sourceWindow);
            if (_sourceWindow == 0 || !_ownsSource)
            {
                Record("controlled Explorer source", "BLOCKED", new { Hwnd = (long)_sourceWindow, Owned = _ownsSource, Reason = "A separate fixture Explorer window could not be verified.", ExpectedPath = _sourcePath, ExplorerProbe = _lastExplorerProbe });
                return Finish();
            }
            Record("controlled Explorer source", "PASS", new { Hwnd = (long)_sourceWindow, Path = _sourcePath });
            if (_features || _automatic)
            {
                var existing = ExplorerWindows().Select(x => x.Handle).ToHashSet();
                _otherPath = Directory.CreateDirectory(Path.Combine(fixtureRoot, "Diğer Kaynak İkinci")).FullName;
                OpenExplorer(_otherPath);
                PumpUntil(() =>
                {
                    _otherWindow = ExplorerWindows().FirstOrDefault(x => SamePath(x.Path, _otherPath) && !existing.Contains(x.Handle))?.Handle ?? 0;
                    return _otherWindow != 0;
                }, 6000);
                if (_otherWindow == 0) throw new Exception("A separate second fixture Explorer window could not be verified.");
            }
            if (_features)
            {
                if (!ProfilePaths.Select(new(fixtureRoot, true, false), out var profileError)) throw new Exception(profileError);
                _productLogPath = Diagnostics.LogPath;
                _context = new FolderAppContext(Path.Combine(fixtureRoot, "shortcut.json"));
                _productId = Environment.ProcessId;
                _binding = ShortcutBinding.FromKeys(Keys.Control | Keys.Shift | Keys.K)!;
                string? error = _context.ApplyShortcut(_binding);
                Record("custom shortcut applied with isolated settings", error is null ? "PASS" : "FAIL", error);
            }
            else
            {
                string profileDirectory = Directory.CreateDirectory(Path.Combine(fixtureRoot, "Profil Çınar")).FullName;
                _productLogPath = Path.Combine(profileDirectory, "activity.log");
                if (_automatic && !new PreferencesStore(Path.Combine(profileDirectory, "folders.json")).SetAutoSwitch(!_automaticOff, out _))
                    throw new Exception("Cannot prepare isolated automatic profile.");
                _binding = ShortcutBinding.Default;
                var startInfo = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
                startInfo.ArgumentList.Add("--profile-dir"); startInfo.ArgumentList.Add(profileDirectory);
                _product = Process.Start(startInfo);
                if (_product is null) throw new InvalidOperationException("Product process did not start.");
                _productId = _product.Id;
                PumpUntil(() => FindBar(false) != 0 || _product.HasExited, 4000);
                if (_product.HasExited) throw new InvalidOperationException("Product exited during startup: " + _product.ExitCode);
            }
            var firstBar = FindBar(false);
            if (firstBar != 0) _productThread = Native.GetWindowThreadProcessId(firstBar, out _);
            Record(_features ? "isolated in-process feature test" : "separate product process", "PASS", new { Pid = _productId, Exe = _features ? null : exe, ParentPid = Environment.ProcessId });
            FocusFixtureSource();
            PumpUntil(() => false, 1000);
            RunDialog(false, initial, physicalClick: true);
            PumpUntil(() => false, 400);
            Record("product survives first dialog close", _product?.HasExited == true ? "FAIL" : "PASS", new { Pid = _productId, BarHwndAfterClose = (long)FindBar(false) });
            if (_product?.HasExited != true)
            {
                FocusFixtureSource(); PumpUntil(() => false, 400);
                RunDialog(true, initial, physicalClick: false);
            }
            if (_features)
            {
                FocusFixtureSource(); PumpUntil(() => false, 400);
                RunDialog(false, initial, physicalClick: true, chooseOther: true);
                FocusWindow(_otherWindow);
                PumpUntil(() => false, 800);
                string originalPath = _sourcePath;
                try { _sourcePath = _otherPath; RunDialog(false, initial, physicalClick: true); }
                finally { _sourcePath = originalPath; }
            }
        }
        catch (Exception e) { Record("harness exception", "FAIL", e.ToString()); }
        finally
        {
            if (_ownsSource && Native.IsWindow(_sourceWindow) && ExplorerWindows().Any(x => x.Handle == _sourceWindow && SamePath(x.Path, _sourcePath)))
                PostMessage(_sourceWindow, 0x10, 0, 0); // Close only our new, still-identical fixture window.
            if (_otherWindow != 0 && ExplorerWindows().Any(x => x.Handle == _otherWindow && SamePath(x.Path, _otherPath))) PostMessage(_otherWindow, 0x10, 0, 0);
            _context?.Dispose();
            if (_product is not null)
            {
                if (!keep && !_product.HasExited)
                {
                    if (_productThread != 0) PostThreadMessage(_productThread, 0x12, 0, 0);
                    PumpUntil(() => _product.HasExited, 1500);
                    if (!_product.HasExited)
                    {
                        _product.Kill();
                        Record("owned test product cleanup", "INFO", "The process launched by this harness was stopped after its message loop did not exit.");
                    }
                }
                _product.Dispose();
            }
        }
        return Finish();
    }

    private static void RunDialog(bool save, string initial, bool physicalClick, bool chooseOther = false)
    {
        string mode = chooseOther ? "OpenMenu" : save ? "Save" : "Open";
        string destination = chooseOther || (_automatic && save && !_automaticOff) ? _otherPath : _sourcePath;
        if (_automaticNoop) initial = destination;
        using var owner = new Form { Text = "KlasorGecis AppHarness owner", ShowInTaskbar = false, StartPosition = FormStartPosition.CenterScreen, Size = new Size(320, 180) };
        owner.Show();
        using FileDialog dialog = save ? new SaveFileDialog() : new OpenFileDialog();
        dialog.Title = "KlasorGecis App Test - " + mode;
        dialog.InitialDirectory = initial; dialog.FileName = "rapor-Çınar 100% #1.txt";
        dialog.Filter = "Text files|*.txt|All files|*.*"; dialog.FilterIndex = 2;
        dialog.AutoUpgradeEnabled = true; dialog.RestoreDirectory = true;
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        var clock = Stopwatch.StartNew();
        bool started = false, finished = false;
        nint targetHwnd = 0;
        string dialogLog = ReadLog();
        timer.Tick += async (_, _) =>
        {
            if (started || clock.ElapsedMilliseconds < 650) return;
            targetHwnd = FindOwnDialog(dialog.Title);
            if (targetHwnd == 0)
            {
                if (clock.ElapsedMilliseconds > 10000)
                {
                    started = true; timer.Stop();
                    Record(mode + " native dialog appears", "BLOCKED", "No matching native dialog appeared within ten seconds.");
                    owner.Close();
                }
                return;
            }
            started = true;
            Native.WinEventProc? focusCallback = null;
            nint hook = 0;
            var focusEvents = new List<object>();
            bool productActivated = false;
            try
            {
                FocusOwnedDialog(targetHwnd); // One bounded test setup focus request only.
                var target = DialogTarget.Detect(targetHwnd);
                if (target is null) { Record(mode + " target detection", "FAIL", new { Hwnd = (long)targetHwnd }); return; }
                nint bar = 0, button = 0;
                for (int i = 0; i < 70; i++)
                {
                    if (!target.IsCurrent()) { Record(mode + " setup foreground", "BLOCKED", new { Foreground = (long)Native.GetForegroundWindow(), Target = (long)targetHwnd }); return; }
                    bar = FindBar(true);
                    button = bar == 0 ? 0 : Native.Children(bar).FirstOrDefault(h => Native.IsWindowVisible(h) && Native.IsWindowEnabled(h) &&
                        Native.Class(h).Contains("BUTTON", StringComparison.OrdinalIgnoreCase) && Native.Title(h).Contains(Path.GetFileName(_sourcePath), StringComparison.OrdinalIgnoreCase));
                    if (button != 0 && Native.Title(button).Contains(Path.GetFileName(_sourcePath), StringComparison.OrdinalIgnoreCase)) break;
                    await Task.Delay(100);
                }
                if (bar == 0 || button == 0 || !Native.Title(button).Contains(Path.GetFileName(_sourcePath), StringComparison.OrdinalIgnoreCase))
                {
                    Record(mode + " actual product folder row", "FAIL", new { Bar = (long)bar, Button = (long)button, Title = button == 0 ? null : Native.Title(button), Expected = _sourcePath, Log = NewLog(dialogLog) });
                    return;
                }
                _productThread = Native.GetWindowThreadProcessId(bar, out _);
                Record(mode + " actual product folder row", "PASS", new { ProductPid = _productId, Hwnd = (long)bar, Button = (long)button, Text = Native.Title(button), Bounds = Native.Frame(bar), Dialog = (long)targetHwnd });
                CaptureScreen(targetHwnd, bar, mode.ToLowerInvariant() + "-before.png");
                var before = Capture(target);
                string inputLog = ReadLog();
                if (!physicalClick && NewLog(dialogLog).Contains("shortcut-unavailable", StringComparison.Ordinal))
                {
                    Record(mode + " Ctrl+G attribution", "BLOCKED", "The product reports Ctrl+G already registered by another application. The second dialog will exercise the physical row instead.");
                    physicalClick = true;
                }
                focusCallback = (_, ev, h, _, _, _, _) =>
                {
                    if (ev != 3 || h == 0) return;
                    Native.GetWindowThreadProcessId(h, out var pid);
                    if (h == bar) productActivated = true;
                    focusEvents.Add(new { Hwnd = (long)h, Pid = pid });
                };
                hook = Native.SetWinEventHook(3, 3, 0, focusCallback, 0, 0, 0);
                if (!target.IsCurrent() || !Native.ModifiersReleased())
                {
                    Record(mode + " input guard", "BLOCKED", new { Current = target.IsCurrent(), Foreground = (long)Native.GetForegroundWindow(),
                        Target = (long)target.Handle, Modifiers = new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Select(k => new { Key = k, Down = (Native.GetAsyncKeyState(k) & 0x8000) != 0 }).ToArray() }); return;
                }
                bool sent;
                string interaction;
                if (_automatic)
                {
                    Native.SendMessageTimeout(target.Handle, 0x28, target.FilenameEdit, 1, 2, 150, out _);
                    await Task.Delay(350);
                    Results.Add(new { Name = mode + " automatic setup focus", Status = "INFO",
                        Eligible = DialogInteraction.TryCapture(target, before.Filename) is not null,
                        Focus = (long)Native.Focus(target.ThreadId), FilenameEdit = (long)target.FilenameEdit,
                        Current = target.IsCurrent(), FocusClass = Native.Class(Native.Focus(target.ThreadId)) });
                    FocusWindow(_sourceWindow);
                    await Task.Delay(350);
                    if (save && !_automaticOff) { FocusWindow(_otherWindow); await Task.Delay(350); }
                    if (_automaticInterrupted)
                    {
                        using var unrelated = new Form { Text = "KlasorGecis unrelated fixture", Size = new Size(280, 140) };
                        unrelated.Show(); LiveHarness.FocusTestWindow(unrelated.Handle); await Task.Delay(250);
                    }
                    FocusOwnedDialog(targetHwnd);
                    Results.Add(new { Name = mode + " automatic return focus", Status = "INFO",
                        Eligible = DialogInteraction.TryCapture(target, before.Filename) is not null,
                        Focus = (long)Native.Focus(target.ThreadId), FilenameEdit = (long)target.FilenameEdit,
                        Current = target.IsCurrent(), FocusClass = Native.Class(Native.Focus(target.ThreadId)) });
                    sent = true;
                    interaction = "automatic Explorer return";
                    if (_automaticOff)
                    {
                        await Task.Delay(700);
                        Record(mode + " disabled automatic mode performs no navigation", target.IsCurrent() &&
                            SamePath(target.CurrentFolder(), before.Folder) && !NewLog(inputLog).Contains(" navigation ok;", StringComparison.Ordinal) ? "PASS" : "FAIL", null);
                        sent = SendShortcut(_binding); interaction = "manual shortcut with automatic mode off";
                    }
                    if (_automaticNoop)
                    {
                        await Task.Delay(1300);
                        Record(mode + " already-current automatic return clears progress and leaves row enabled",
                            target.IsCurrent() && SamePath(target.CurrentFolder(), before.Folder) && Native.IsWindowEnabled(button) &&
                            !NewLog(inputLog).Contains(" navigation ok;", StringComparison.Ordinal) ? "PASS" : "FAIL", null);
                        finished = true; return;
                    }
                    if (_automaticInterrupted)
                    {
                        await Task.Delay(1300);
                        var unchanged = Capture(target);
                        Record(mode + " unrelated foreground interrupts automatic intent without changing the dialog",
                            target.IsCurrent() && SamePath(unchanged.Folder, before.Folder) && unchanged.Filename == before.Filename &&
                            unchanged.Filter == before.Filter && Native.IsWindowEnabled(button) &&
                            !NewLog(inputLog).Contains(" navigation ok;", StringComparison.Ordinal) ? "PASS" : "FAIL", null);
                        finished = true; return;
                    }
                }
                else if (chooseOther)
                {
                    var arrow = Native.Children(bar).FirstOrDefault(h => Native.IsWindowVisible(h) &&
                        Native.Class(h).Contains("BUTTON", StringComparison.OrdinalIgnoreCase) && Native.Title(h) == "▾");
                    sent = arrow != 0 && ClickVerifiedRow(target, bar, arrow);
                    await Task.Delay(150);
                    var menu = (ContextMenuStrip)typeof(FolderAppContext).GetField("_foldersMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_context)!;
                    if (menu.Items["ToggleFavorite"] is ToolStripItem addFavorite)
                    {
                        var addPoint = menu.PointToScreen(new Point(addFavorite.Bounds.Left + addFavorite.Bounds.Width / 2, addFavorite.Bounds.Top + addFavorite.Bounds.Height / 2));
                        if (!ClickMenuPoint(addPoint, menu.Handle, target)) throw new Exception("Favorite action could not be clicked.");
                        await Task.Delay(120);
                        Record("favorite action persists selected Explorer folder", new PreferencesStore(Path.Combine(_fixtureRoot, "folders.json")).Current.Favorites.Any(f => f.Path == _sourcePath) ? "PASS" : "FAIL", null);
                        if (!ClickVerifiedRow(target, bar, arrow)) throw new Exception("Folder menu could not reopen.");
                        await Task.Delay(100);
                    }
                    var item = menu.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.ToolTipText == _otherPath && i.Tag is FolderRequest);
                    Record("folder menu preserves file dialog foreground", menu.Visible && target.IsCurrent() ? "PASS" : "FAIL", null);
                    if (menu.Visible)
                    {
                        var captureBounds = Rectangle.Union(Native.Frame(targetHwnd), Rectangle.Union(Native.Frame(bar), menu.Bounds));
                        using var image = new Bitmap(captureBounds.Width, captureBounds.Height);
                        using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(captureBounds.Location, Point.Empty, captureBounds.Size);
                        image.Save(Path.Combine(Path.GetDirectoryName(_output)!, "feature-menu.png"), ImageFormat.Png);
                    }
                    if (sent && item is not null && menu.Visible && target.IsCurrent())
                    {
                        var point = menu.PointToScreen(new Point(item.Bounds.Left + item.Bounds.Width / 2, item.Bounds.Top + item.Bounds.Height / 2));
                        sent = ClickMenuPoint(point, menu.Handle, target);
                    }
                    else sent = false;
                    interaction = "other-folder physical menu selection";
                }
                else
                {
                    sent = physicalClick ? ClickVerifiedRow(target, bar, button) : SendShortcut(_binding);
                    interaction = physicalClick ? "physical click" : _binding.DisplayText;
                }
                if (!sent) { Record(mode + " " + interaction, "BLOCKED", "Verified input could not be delivered to the product row."); return; }
                bool lostFocus = false;
                for (int i = 0; i < 60; i++)
                {
                    await Task.Delay(100);
                    if (!target.IsCurrent()) { lostFocus = true; break; }
                    if (SamePath(target.CurrentFolder(), destination) && NewLog(inputLog).Contains(" navigation ok;", StringComparison.Ordinal)) break;
                }
                var after = Capture(target);
                string events = NewLog(inputLog);
                bool logProof = events.Contains(" navigation ok;", StringComparison.Ordinal);
                bool preserved = before.Filename is not null && before.Filter is not null && before.Filename == after.Filename && before.Filter == after.Filter;
                bool success = !lostFocus && SamePath(after.Folder, destination) && after.Foreground && preserved && logProof && !productActivated;
                Record(mode + " " + interaction + " changes same dialog", lostFocus ? "BLOCKED" : success ? "PASS" : "FAIL", new { Before = before, After = after, Destination = destination, ProductNavigationLog = events, ProductLogProof = logProof, ProductActivated = productActivated, FocusEvents = focusEvents });
                if (after.Foreground) CaptureScreen(targetHwnd, bar, mode.ToLowerInvariant() + "-after.png");
                finished = true;
            }
            catch (Exception e) { Record(mode + " interaction exception", "FAIL", e.ToString()); }
            finally
            {
                if (hook != 0) Native.UnhookWinEvent(hook);
                GC.KeepAlive(focusCallback);
                timer.Stop();
                if (IsOwnWindow(targetHwnd)) PostMessage(targetHwnd, 0x10, 0, 0); // Cancel, never choose Open/Save.
            }
        };
        timer.Start();
        var result = dialog.ShowDialog(owner);
        timer.Stop();
        Record(mode + " dialog canceled without opening/saving", result == DialogResult.Cancel ? "PASS" : "FAIL", new { ModalResult = result.ToString(), InteractionFinished = finished, DialogHwnd = (long)targetHwnd });
    }

    private static bool ClickVerifiedRow(DialogTarget target, nint bar, nint button)
    {
        var bounds = Native.Frame(button);
        if (bounds.IsEmpty || !target.IsCurrent()) return false;
        POINT point = new() { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
        if (!OwnBarPoint(point, bar)) return false;
        GetCursorPos(out var original);
        bool moved = SetCursorPos(point.X, point.Y);
        try
        {
            if (!moved || !OwnBarPoint(point, bar) || !target.IsCurrent() || !Native.ModifiersReleased()) return false;
            mouse_event(0x0002, 0, 0, 0, 0);
            mouse_event(0x0004, 0, 0, 0, 0);
            return true;
        }
        finally
        {
            if (moved && GetCursorPos(out var now) && now.X == point.X && now.Y == point.Y)
                SetCursorPos(original.X, original.Y);
        }
    }
    private static bool SendShortcut(ShortcutBinding binding)
    {
        var keys = new List<(ushort Key, bool Up)>();
        if ((binding.Modifiers & 2) != 0) keys.Add((0x11, false));
        if ((binding.Modifiers & 1) != 0) keys.Add((0x12, false));
        if ((binding.Modifiers & 4) != 0) keys.Add((0x10, false));
        keys.Add(((ushort)binding.Key, false)); keys.Add(((ushort)binding.Key, true));
        if ((binding.Modifiers & 4) != 0) keys.Add((0x10, true));
        if ((binding.Modifiers & 1) != 0) keys.Add((0x12, true));
        if ((binding.Modifiers & 2) != 0) keys.Add((0x11, true));
        return Native.Keys(keys.ToArray());
    }
    private static bool ClickMenuPoint(Point point, nint menu, DialogTarget target)
    {
        GetCursorPos(out var original);
        var hit = WindowFromPoint(new POINT { X = point.X, Y = point.Y });
        if ((hit != menu && !Native.IsChild(menu, hit)) || !target.IsCurrent()) return false;
        SetCursorPos(point.X, point.Y);
        try { mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0); return true; }
        finally
        {
            if (GetCursorPos(out var now) && now.X == point.X && now.Y == point.Y) SetCursorPos(original.X, original.Y);
        }
    }
    private static bool OwnBarPoint(POINT p, nint bar)
    {
        var h = WindowFromPoint(p);
        Native.GetWindowThreadProcessId(h, out var pid);
        return pid == _productId && (h == bar || Native.IsChild(bar, h));
    }
    private sealed record State(long Hwnd, string? Folder, string? Filename, int? Filter, bool Foreground);
    private static State Capture(DialogTarget target)
    {
        string? filename = null;
        if (target.FilenameEdit != 0 && Native.TryReadText(target.FilenameEdit, out var text)) filename = text;
        return new((long)target.Handle, target.CurrentFolder(), filename, target.FilterIndex(), target.IsCurrent());
    }
    private static void CaptureScreen(nint dialog, nint bar, string file)
    {
        if (!IsOwnWindow(dialog) || Native.GetForegroundWindow() != dialog || !Native.IsWindowVisible(bar)) return;
        var bounds = Rectangle.Union(Native.Frame(dialog), Native.Frame(bar));
        bounds.Intersect(SystemInformation.VirtualScreen);
        if (bounds.Width < 1 || bounds.Height < 1) return;
        string path = Path.Combine(Path.GetDirectoryName(_output)!, file);
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        bitmap.Save(path, ImageFormat.Png);
        Record("controlled screenshot", "INFO", new { Path = path, Bounds = bounds });
    }
    private static nint FindBar(bool visible)
    {
        if (_productId == 0) return 0;
        nint result = 0;
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            if (pid == _productId && Native.Title(h) == "Klasör Geçiş" && (!visible || Native.IsWindowVisible(h))) { result = h; return false; }
            return true;
        }, 0);
        return result;
    }
    private static nint FindOwnDialog(string title)
    {
        nint result = 0;
        Native.EnumWindows((h, _) => { if (IsOwnWindow(h) && Native.Class(h) == "#32770" && Native.Title(h) == title) { result = h; return false; } return true; }, 0);
        return result;
    }
    private static bool IsOwnWindow(nint h) { Native.GetWindowThreadProcessId(h, out var pid); return h != 0 && Native.IsWindow(h) && pid == Environment.ProcessId; }
    private static void FocusOwnedDialog(nint h)
    {
        if (!IsOwnWindow(h)) throw new InvalidOperationException("Focus target is not an owned test window.");
        FocusWindow(h);
    }
    private static void FocusFixtureSource()
    {
        if (_ownsSource && ExplorerWindows().Any(x => x.Handle == _sourceWindow && SamePath(x.Path, _sourcePath))) FocusWindow(_sourceWindow);
    }
    private static void FocusWindow(nint h)
    {
        uint self = GetCurrentThreadId(), foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        uint target = Native.GetWindowThreadProcessId(h, out _);
        bool attached = foreground != 0 && foreground != self && AttachThreadInput(self, foreground, true);
        bool targetAttached = target != 0 && target != self && target != foreground && AttachThreadInput(self, target, true);
        try { BringWindowToTop(h); SetForegroundWindow(h); }
        finally
        {
            if (targetAttached) AttachThreadInput(self, target, false);
            if (attached) AttachThreadInput(self, foreground, false);
        }
    }
    private sealed record ExplorerWindow(nint Handle, string Path);
    private static List<ExplorerWindow> ExplorerWindows()
    {
        var result = new List<ExplorerWindow>();
        var observed = new List<object>();
        object? shell = null, windows = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            windows = ((dynamic)shell!).Windows();
            int count = ((dynamic)windows).Count;
            for (int i = 0; i < count; i++)
            {
                object? window = null, document = null, folder = null, item = null;
                try
                {
                    window = ((dynamic)windows).Item(i);
                    if (window is null) continue;
                    string name = ((dynamic)window!).FullName;
                    if (!string.Equals(Path.GetFileName(name), "explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    long handle = (long)((dynamic)window).HWND;
                    // Explorer LocationURL can use legacy ANSI percent escapes (observed %C7 for Ç).
                    // Read the shell's Unicode filesystem path instead of decoding that mixed URL.
                    document = ((dynamic)window).Document;
                    if (document is null) continue;
                    folder = ((dynamic)document!).Folder;
                    if (folder is null) continue;
                    item = ((dynamic)folder!).Self;
                    if (item is null) continue;
                    string rawPath = ((dynamic)item!).Path;
                    bool valid = CoreRules.TryNormalizePath(rawPath, out var path);
                    if (observed.Count < 64 && rawPath.StartsWith(_fixtureRoot, StringComparison.OrdinalIgnoreCase)) observed.Add(new { Hwnd = handle, Path = rawPath, Valid = valid });
                    if (valid) result.Add(new((nint)handle, path));
                }
                catch (COMException e) { if (observed.Count < 64) observed.Add(new { Index = i, Error = e.Message, e.HResult }); }
                finally { Release(item); Release(folder); Release(document); Release(window); }
            }
        }
        finally { Release(windows); Release(shell); _lastExplorerProbe = observed.ToArray(); }
        return result;
    }
    private static void OpenExplorer(string path)
    {
        object? shell = null;
        try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); ((dynamic)shell!).Explore(path); }
        finally { Release(shell); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private static bool SamePath(string? a, string? b) => a is not null && b is not null && string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    private static bool PumpUntil(Func<bool> predicate, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        do { if (predicate()) return true; Application.DoEvents(); Thread.Sleep(25); } while (watch.ElapsedMilliseconds < milliseconds);
        return predicate();
    }
    private static string ReadLog()
    {
        try { using var stream = new FileStream(_productLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        catch (IOException) { return ""; }
    }
    private static string NewLog(string before) { string after = ReadLog(); return after.StartsWith(before, StringComparison.Ordinal) ? after[before.Length..] : after; }
    private static string FindProductExe()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "src", "KlasorGecis", "bin", "Release", "net10.0-windows", "KlasorGecis.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Use --exe to select the actual built KlasorGecis.exe.");
    }
    private static string? Option(string[] args, string name) { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static void Record(string name, string status, object? detail)
    {
        Results.Add(new { Name = name, Status = status, Detail = detail });
        if (status == "FAIL") _failures++; if (status == "BLOCKED") _blocked++;
        Console.WriteLine(status + " " + name);
    }
    private static int Finish()
    {
        File.WriteAllText(_output, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow,
            Version = typeof(ExplorerSource).Assembly.GetName().Version?.ToString(),
            ProductAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ExplorerSource).Assembly.Location))),
            PortableExeSha256 = _testedExe is null ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(_testedExe))),
            Failures = _failures, Blocked = _blocked, Results }, Json));
        return _failures > 0 ? 1 : _blocked > 0 ? 2 : 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint h, uint message, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint h);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
