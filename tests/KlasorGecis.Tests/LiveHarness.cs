using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using KlasorGecis;

internal static class LiveHarness
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly List<object> Results = [];
    private static int _failures;
    internal static int Run(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args[0] == "--localization-suite") return LocalizationHarness.Run(args);
        if (args[0] is "--app-suite" or "--feature-suite" or "--automatic-suite" or "--automatic-off-suite" or "--automatic-noop-suite" or "--automatic-interrupted-suite") return AppHarness.Run(args);
        if (args[0] == "--source-suite") return SourceHarness.Run(args);
        if (args[0] == "--open-fixture") return SourceHarness.RunOpenFixture(args);
        if (args[0] == "--settings-suite") return RunSettings(args);
        if (args[0] == "--performance-suite") return PerformanceHarness.Run(args);
        if (args[0] == "--saved-suite") return SavedFolderHarness.Run(args);
        if (args[0] == "--appearance-suite") return AppearanceHarness.Run(args);
        if (args[0] == "--bar-lifecycle-suite") return RunBarLifecycle(args);
        if (args[0] == "--native-settings-suite") return NativeSettingsHarness.Run(args);
        var outIndex = Array.IndexOf(args, "--out");
        string output = outIndex >= 0 && outIndex + 1 < args.Length ? Path.GetFullPath(args[outIndex + 1]) : Path.GetFullPath("live-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            if (args[0] == "--source-snapshot")
            {
                using var reader = new ExplorerSource();
                var folders = reader.GetFoldersAsync(CancellationToken.None).GetAwaiter().GetResult();
                File.WriteAllText(output, JsonSerializer.Serialize(folders.Select(f => new { Hwnd = (long)f.WindowHandle, f.ProcessId, f.Path, f.DisplayName }), Json));
                Console.WriteLine($"SOURCES {folders.Count}"); return 0;
            }
            if (args[0] is "--hold-open" or "--hold-save")
                return Hold(args[0] == "--hold-save", output);
            if (args[0] != "--live-suite") throw new ArgumentException("Unknown harness mode");
            var fixtureRoot = Path.Combine(Path.GetDirectoryName(output)!, "Fixtures");
            Directory.CreateDirectory(fixtureRoot);
            var initial = Directory.CreateDirectory(Path.Combine(fixtureRoot, "Initial")).FullName;
            var destination = Directory.CreateDirectory(Path.Combine(fixtureRoot, "Hedef Çınar #1 %25")).FullName;
            using (var ordinary = new Form())
                Record("reject non-file form", DialogTarget.Detect(ordinary.Handle) is null, null);
            RunDialog(false, initial, destination);
            RunDialog(true, initial, destination);
        }
        catch (Exception e) { Record("harness exception", false, e.ToString()); }
        File.WriteAllText(output, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow,
            Version = typeof(ExplorerSource).Assembly.GetName().Version?.ToString(),
            ProductAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ExplorerSource).Assembly.Location))),
            Failures = _failures, Results }, Json));
        Console.WriteLine($"LIVE RESULT {_failures} failed");
        return _failures == 0 ? 0 : 1;
    }

    private static int RunBarLifecycle(string[] args)
    {
        int index = Array.IndexOf(args, "--out");
        string output = Path.GetFullPath(args[index + 1]);
        using var owner = new Form { Text = "KlasorGecis native lifecycle fixture", Size = new Size(600, 300), StartPosition = FormStartPosition.CenterScreen };
        owner.Show(); FocusTestWindow(owner.Handle);
        using var bar = new FolderBar();
        bar.SetOtherFoldersAvailable(true);
        var bounds = new Rectangle(owner.Left, owner.Bottom, 500, 50);
        bar.AttachTo(owner.Handle, bounds); Application.DoEvents();
        Record("initial saved-folder row has native menu button", Native.Children(bar.Handle).Any(h => Native.Title(h) == "▾" && Native.IsWindowVisible(h)), null);
        // Destroy the owned native surface without disposing its managed Form,
        // as Windows does when a foreign native owner is destroyed.
        nint destroyed = bar.Handle;
        bool destroyedOk = DestroyWindow(destroyed); Application.DoEvents();
        Record("fixture really destroys only its native row surface", destroyedOk && !Native.IsWindow(destroyed), null);
        bar.AttachTo(owner.Handle, bounds); Application.DoEvents();
        Record("row recreates native buttons after owner destroys its surface", Native.Children(bar.Handle).Any(h => Native.Title(h) == "▾" && Native.IsWindowVisible(h) && Native.IsWindowEnabled(h)), null);
        Record("row recreates its main native button too", Native.Children(bar.Handle).Any(h =>
            Native.Class(h).Contains("BUTTON", StringComparison.OrdinalIgnoreCase) && Native.Title(h) != "▾" && Native.IsWindowVisible(h)), null);
        Record("recreated row never activates over its owner", Native.GetForegroundWindow() == owner.Handle, null);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow,
            ProductAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ExplorerSource).Assembly.Location))),
            Failures = _failures, Results }, Json));
        return _failures == 0 ? 0 : 1;
    }

    private static int RunSettings(string[] args)
    {
        int index = Array.IndexOf(args, "--out");
        string output = Path.GetFullPath(index >= 0 ? args[index + 1] : "settings-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        bool passed = false;
        string? error = null;
        ShortcutBinding? saved = null;
        using var dialog = new ShortcutDialog(ShortcutBinding.Default, binding => { saved = binding; return null; });
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        var watch = Stopwatch.StartNew();
        bool started = false;
        timer.Tick += async (_, _) =>
        {
            if (started || watch.ElapsedMilliseconds < 400) return;
            started = true;
            try
            {
                var entry = dialog.Controls.OfType<TextBox>().Single();
                FocusTestWindow(dialog.Handle);
                entry.Focus();
                await Task.Delay(80);
                if (Native.GetForegroundWindow() != dialog.Handle || !entry.Focused || !Native.ModifiersReleased())
                    throw new Exception("Settings input setup could not be verified.");
                Native.Keys((0x11, false), (0x10, false), (0x4B, false), (0x4B, true), (0x10, true), (0x11, true));
                await Task.Delay(120);
                if (entry.Text != "Ctrl+Shift+K") throw new Exception("Shortcut chord was not captured.");
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.ChangeExtension(output, ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                Native.Keys((0x09, false), (0x09, true));
                await Task.Delay(70);
                Native.Keys((0x09, false), (0x09, true));
                await Task.Delay(70);
                var save = dialog.Controls.OfType<Button>().Single(b => b.Text == "Kaydet");
                if (!save.Focused) throw new Exception("Save button could not be reached with Tab.");
                if (!Native.Keys((0x0D, false), (0x0D, true))) throw new Exception("Save input could not be delivered.");
            }
            catch (Exception e) { error = e.Message; }
            finally { timer.Stop(); if (error is not null && !dialog.IsDisposed) dialog.Close(); }
        };
        timer.Start(); dialog.ShowDialog(); timer.Stop();
        passed = saved?.DisplayText == "Ctrl+Shift+K" && dialog.DialogResult == DialogResult.OK && error is null;
        File.WriteAllText(output, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow,
            Pass = passed, Error = error, Captured = saved?.DisplayText, Test = "Physical chord capture, Tab navigation and Enter to save" }, Json));
        return passed ? 0 : 1;
    }

    private static void RunDialog(bool save, string initial, string destination)
    {
        string mode = save ? "Save" : "Open";
        using var owner = new Form { Text = "KlasorGecis test owner", ShowInTaskbar = false, Size = new Size(320, 180), StartPosition = FormStartPosition.CenterScreen };
        owner.Show(); owner.Activate();
        using FileDialog dialog = save ? new SaveFileDialog() : new OpenFileDialog();
        dialog.Title = "KlasorGecis Test - " + mode;
        dialog.InitialDirectory = initial;
        dialog.FileName = "rapor-Çınar 100% #1.txt";
        dialog.Filter = "Text files|*.txt|All files|*.*";
        dialog.FilterIndex = 2;
        dialog.AutoUpgradeEnabled = true;
        dialog.RestoreDirectory = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        var elapsed = Stopwatch.StartNew();
        bool started = false, finished = false, requestedFocus = false;
        DialogTarget? target = null;
        timer.Tick += async (_, _) =>
        {
            if (started) return;
            if (elapsed.ElapsedMilliseconds < 750) return;
            var hwnd = FindOwnDialog(dialog.Title);
            if (hwnd == 0)
            {
                if (elapsed.Elapsed.TotalSeconds > 10) { timer.Stop(); Record(mode + " window timeout", false, null); }
                return;
            }
            target = DialogTarget.Detect(hwnd);
            if (!requestedFocus)
            {
                requestedFocus = true;
                FocusTestWindow(hwnd); // Test setup only; the product never forces foreground activation.
                return;
            }
            if (target is null || !target.IsCurrent())
            {
                if (elapsed.Elapsed.TotalSeconds > 5)
                {
                    started = true; Record(mode + " detection/foreground", false, new { Hwnd = (long)hwnd, Foreground = (long)Native.GetForegroundWindow() });
                    PostMessage(hwnd, 0x10, 0, 0);
                }
                return;
            }
            started = true;
            try
            {
                var before = Capture(target);
                using (var input = new InputMonitor())
                {
                    long epoch = input.Epoch;
                    Native.KeysWithStamp(out uint batch, (0x87, false), (0x87, true));
                    await Task.Delay(60);
                    Record(mode + " input monitor acknowledges only its marked batch", input.Available && input.Received(batch) && input.Unchanged(epoch), null);
                    KeybdEvent(0x87, 0, 0, 0); KeybdEvent(0x87, 0, 2, 0);
                    await Task.Delay(60);
                    Record(mode + " input monitor detects unmarked new input", input.Available && !input.Unchanged(epoch), null);
                }
                var invalid = await DialogNavigator.NavigateAsync(target, "shell:Desktop", deadline.Token);
                Record(mode + " refuses virtual path", !invalid.Success && invalid.Code == "invalid-path", invalid);
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                var cancelled = await DialogNavigator.NavigateAsync(target, destination, canceled.Token);
                Record(mode + " canceled request sends no navigation", !cancelled.Success && cancelled.Code == "cancelled" && target.CurrentFolder() == before.Folder, cancelled);
                using (var interrupt = new Form { Text = "KlasorGecis in-flight focus test", Size = new Size(260, 120) })
                {
                    if (!Native.ModifiersReleased()) throw new InvalidOperationException("A physical modifier is held; live test cannot safely proceed.");
                    Native.Keys((0x11, false));
                    Task<NavigationResult> pending;
                    try
                    {
                        pending = DialogNavigator.NavigateAsync(target, destination, deadline.Token);
                        await Task.Delay(80);
                        interrupt.Show(); interrupt.Activate();
                        await Task.Delay(80);
                    }
                    finally { Native.Keys((0x11, true)); }
                    var interrupted = await pending;
                    Record(mode + " focus loss during navigation sends no Enter", !interrupted.Success &&
                        Native.GetForegroundWindow() == interrupt.Handle && target.CurrentFolder() == before.Folder, interrupted);
                    interrupt.Close();
                    FocusTestWindow(hwnd);
                    await Task.Delay(80);
                }
                Native.SendMessageTimeout(target.Handle, 0x28, target.FilenameEdit, 1, 2, 150, out _);
                Native.SendMessageTimeout(target.FilenameEdit, 0xB1, 3, 7, 2, 150, out _);
                var result = await DialogNavigator.NavigateAsync(target, destination, deadline.Token);
                var after = Capture(target);
                Record(mode + " navigates preserving filename and filter", result.Success && target.IsCurrent() &&
                    before.Filename is not null && before.Filter is not null &&
                    before.Filename == after.Filename && before.Filter == after.Filter && after.Folder == destination,
                    new { Before = before, After = after, Result = result, Focus = Describe(Native.Focus(target.ThreadId)),
                        Controls = result.Success ? null : Native.Children(target.Handle).Where(h => Native.Class(h) is "Edit" or "ComboBox" or "ComboBoxEx32" or "Address Band Root" or "ToolbarWindow32").Select(Describe).ToArray() });
                Record(mode + " restores filename focus and exact selection", result.Success &&
                    Native.Focus(target.ThreadId) == target.FilenameEdit && Native.TryReadSelection(target.FilenameEdit, out int restoredFirst, out int restoredLast) &&
                    restoredFirst == 3 && restoredLast == 7, new { result.Interaction });
                for (int position = 0; position < 4; position++)
                {
                    Native.TryReadText(target.FilenameEdit, out string name);
                    int first = position == 0 ? 0 : position == 1 ? name.Length / 2 : position == 2 ? name.Length : 3;
                    int last = position == 3 ? 7 : first;
                    Native.SendMessageTimeout(target.Handle, 0x28, target.FilenameEdit, 1, 2, 150, out _);
                    Native.SendMessageTimeout(target.FilenameEdit, 0xB1, first, last, 2, 150, out _);
                    var caret = await DialogNavigator.NavigateAsync(target, position % 2 == 0 ? initial : destination, deadline.Token);
                    Record(mode + " preserves caret/selection position " + position, caret.Success && Native.Focus(target.ThreadId) == target.FilenameEdit &&
                        Native.TryReadSelection(target.FilenameEdit, out int actualFirst, out int actualLast) && actualFirst == first && actualLast == last, null);
                }
                Native.TryReadText(target.FilenameEdit, out string originalName);
                Native.Keys((0x11, false)); await Task.Delay(40);
                Task<NavigationResult> editPending;
                try
                {
                    editPending = DialogNavigator.NavigateAsync(target, initial, deadline.Token);
                    Native.SetText(target.FilenameEdit, "yeni-kullanıcı-metni-Çınar.txt");
                    await Task.Delay(40);
                }
                finally { Native.Keys((0x11, true)); }
                var edited = await editPending;
                Record(mode + " newer filename edit cancels pending transition without restoring old text",
                    !edited.Success && edited.Code == "interaction-changed" && target.CurrentFolder() == destination &&
                    Native.TryReadText(target.FilenameEdit, out string editedName) && editedName == "yeni-kullanıcı-metni-Çınar.txt", null);
                Native.SetText(target.FilenameEdit, originalName);
                Native.SendMessageTimeout(target.FilenameEdit, 0xB1, 3, 7, 2, 150, out _);
                using (var input = new InputMonitor())
                {
                    long epoch = input.Epoch;
                    var snapshot = DialogInteraction.TryCapture(target, originalName);
                    Native.KeysWithStamp(out uint batch, (0x11, false), (0x4C, false), (0x4C, true), (0x11, true));
                    await Task.Delay(80);
                    nint address = Native.Focus(target.ThreadId);
                    KeybdEvent(0x87, 0, 0, 0); KeybdEvent(0x87, 0, 2, 0); await Task.Delay(60);
                    var restoration = DialogInteraction.TryRestore(target, snapshot, address, input, epoch, batch, deadline.Token);
                    Record(mode + " new input prevents delayed focus and selection restore", snapshot is not null &&
                        restoration == InteractionRestore.Skipped && Native.Focus(target.ThreadId) == address, null);
                }
                Native.SendMessageTimeout(target.Handle, 0x28, target.FilenameEdit, 1, 2, 150, out _);
                Native.SendMessageTimeout(target.FilenameEdit, 0xB1, 3, 7, 2, 150, out _);
                uint? beforeNoop = Native.LastInputTick();
                var noop = await DialogNavigator.NavigateAsync(target, destination, deadline.Token);
                Record(mode + " already-current folder sends no keyboard input", noop.Success && noop.Code == "already-there" &&
                    Native.LastInputTick() == beforeNoop && Native.Focus(target.ThreadId) == target.FilenameEdit, null);
                var canceledNoop = await DialogNavigator.NavigateAsync(target, destination, canceled.Token);
                Record(mode + " canceled same-folder request remains canceled", !canceledNoop.Success && canceledNoop.Code == "cancelled", null);
                using var other = new Form { Text = "KlasorGecis focus test", Size = new Size(250, 120), StartPosition = FormStartPosition.CenterScreen };
                other.Show(); other.Activate();
                await Task.Delay(80);
                if (Native.GetForegroundWindow() == other.Handle)
                {
                    var refused = await DialogNavigator.NavigateAsync(target, initial, deadline.Token);
                    Record(mode + " refuses unfocused target", !refused.Success && target.CurrentFolder() == after.Folder, refused);
                }
                else Record(mode + " focus test setup", false, "Owned helper could not acquire focus");
                other.Close();
                finished = true;
            }
            catch (Exception e) { Record(mode + " exception", false, e.ToString()); }
            finally
            {
                timer.Stop();
                if (Native.IsWindow(hwnd)) PostMessage(hwnd, 0x10, 0, 0);
            }
        };
        timer.Start();
        var modalResult = dialog.ShowDialog(owner);
        timer.Stop();
        if (!finished) Record(mode + " sequence completed", false, new { ModalResult = modalResult.ToString(), Started = started });
        if (target is not null)
        {
            var stale = DialogNavigator.NavigateAsync(target, destination, CancellationToken.None).GetAwaiter().GetResult();
            Record(mode + " rejects closed target", !stale.Success && stale.Code == "target-changed", stale);
        }
    }

    private static int Hold(bool save, string output)
    {
        var initial = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(output)!, "HoldInitial")).FullName;
        using FileDialog dialog = save ? new SaveFileDialog() : new OpenFileDialog();
        dialog.Title = "KlasorGecis Hold - " + (save ? "Save" : "Open");
        dialog.InitialDirectory = initial; dialog.FileName = "dosya-adi-korunacak.txt";
        dialog.Filter = "Text files|*.txt|All files|*.*"; dialog.FilterIndex = 2;
        using var timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) =>
        {
            var h = FindOwnDialog(dialog.Title); if (h == 0) return;
            var target = DialogTarget.Detect(h); if (target is null) return;
            File.WriteAllText(output, JsonSerializer.Serialize(new { Pid = Environment.ProcessId, Title = dialog.Title, Hwnd = (long)h, State = Capture(target) }, Json));
            timer.Stop();
        };
        timer.Start(); dialog.ShowDialog(); timer.Stop();
        return 0;
    }

    private sealed record DialogState(string? Folder, string? Filename, int? Filter, bool Open, bool Foreground);
    private static object Describe(nint h) => new { Hwnd = (long)h, Class = Native.Class(h), Title = Native.Title(h), Id = Native.GetDlgCtrlID(h), Parent = (long)Native.GetParent(h), Visible = Native.IsWindowVisible(h) };
    private static DialogState Capture(DialogTarget t)
    {
        string? filename = null;
        if (t.FilenameEdit != 0) Native.TryReadText(t.FilenameEdit, out filename);
        return new(t.CurrentFolder(), filename, t.FilterIndex(), Native.IsWindow(t.Handle), Native.GetForegroundWindow() == t.Handle);
    }
    internal static nint FindOwnDialog(string title)
    {
        nint found = 0;
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            if (pid == Environment.ProcessId && Native.Class(h) == "#32770" && Native.Title(h) == title) { found = h; return false; }
            return true;
        }, 0);
        return found;
    }
    private static void Record(string name, bool pass, object? detail)
    {
        Results.Add(new { Name = name, Pass = pass, Detail = detail });
        if (!pass) _failures++;
        Console.WriteLine((pass ? "PASS " : "FAIL ") + name);
    }
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "keybd_event")] private static extern void KeybdEvent(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    internal static void FocusTestWindow(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != Environment.ProcessId) throw new InvalidOperationException("Test focus target must belong to this harness");
        uint self = GetCurrentThreadId();
        uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        bool attached = foreground != 0 && foreground != self && AttachThreadInput(self, foreground, true);
        try { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); }
        finally { if (attached) AttachThreadInput(self, foreground, false); }
    }
}
