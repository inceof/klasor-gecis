using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using KlasorGecis;

internal static class SourceHarness
{
    private static readonly List<object> Results = [];
    private static int failures, blocked;
    private static string fixture = "";
    private static readonly List<nint> owned = [];

    internal static int Run(string[] args)
    {
        int outputIndex = Array.IndexOf(args, "--out");
        string output = Path.GetFullPath(outputIndex >= 0 ? args[outputIndex + 1] : "source-results.json");
        fixture = Path.Combine(Path.GetDirectoryName(output)!, "SourceFixtures", Guid.NewGuid().ToString("N"));
        string a = Directory.CreateDirectory(Path.Combine(fixture, "Kaynak A Çınar %25")).FullName;
        string b = Directory.CreateDirectory(Path.Combine(fixture, "Kaynak B İkinci")).FullName;
        string changed = Directory.CreateDirectory(Path.Combine(fixture, "Hızlı değişen #1")).FullName;
        string tab = Directory.CreateDirectory(Path.Combine(fixture, "Etkin sekme Özel")).FullName;
        try
        {
            using var reader = new ExplorerSource();
            nint first = Open(a), second = Open(b);
            bool both = Until(() => Has(reader, first, a) && Has(reader, second, b), 8000);
            Record("two real Explorer sources are independently detected", both);
            if (!both) return Finish(output);

            NavigateOwned(first, changed);
            bool fresh = Until(() => Has(reader, first, changed), 6000);
            var snapshot = reader.GetFreshFoldersAsync(CancellationToken.None).GetAwaiter().GetResult();
            Record("fresh scan follows rapid folder change without old path", fresh &&
                snapshot.Any(f => f.WindowHandle == first && f.Path == changed) &&
                !snapshot.Any(f => f.WindowHandle == first && f.Path == a));
            var stale = new FolderLocation(first, snapshot.First(f => f.WindowHandle == first).ProcessId, a, "Kaynak A");
            Record("stale menu choice is rejected after real Explorer navigation", SourceSelection.Resolve(stale, snapshot) is null);

            FocusOwned(first);
            int tabsBefore = Native.Children(first).Count(h => Native.Class(h) == "ShellTabWindowClass");
            if (!Native.Keys((0x11, false), (0x54, false), (0x54, true), (0x11, true))) throw new Exception("Cannot create fixture tab.");
            bool tabCreated = Until(() => Native.Children(first).Count(h => Native.Class(h) == "ShellTabWindowClass") > tabsBefore, 4000);
            if (!tabCreated) Record("Windows 11 tab setup", false, "BLOCKED");
            else
            {
                NavigateTabOwned(first, tab);
                bool active = Until(() => Has(reader, first, tab), 7000);
                var current = reader.GetFreshFoldersAsync(CancellationToken.None).GetAwaiter().GetResult();
                Probe(first, "new-tab", current);
                Record("Windows 11 active tab is the sole source for its window", active &&
                    current.Count(f => f.WindowHandle == first) == 1 &&
                    current.Single(f => f.WindowHandle == first).Path == tab);
                FocusOwned(first);
                Native.Keys((0x11, false), (0x10, false), (0x09, false), (0x09, true), (0x10, true), (0x11, true));
                Record("switching back changes source to previous Explorer tab", Until(() => Has(reader, first, changed), 7000));
                Probe(first, "previous-tab", reader.GetFreshFoldersAsync(CancellationToken.None).GetAwaiter().GetResult());
            }

            CloseOwned(second);
            Record("closed Explorer window is absent from fresh sources", Until(() =>
                !reader.GetFreshFoldersAsync(CancellationToken.None).GetAwaiter().GetResult().Any(f => f.WindowHandle == second), 6000));
        }
        catch (Exception e) { Results.Add(new { Name = "source harness exception", Status = "FAIL", Error = e.ToString() }); failures++; }
        finally { foreach (var window in owned.ToArray()) CloseOwned(window); }
        return Finish(output);
    }

    private static bool Has(ExplorerSource reader, nint window, string path) =>
        reader.GetFreshFoldersAsync(CancellationToken.None).GetAwaiter().GetResult()
            .Any(f => f.WindowHandle == window && string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

    private static void Probe(nint window, string stage, IReadOnlyList<FolderLocation> snapshot)
    {
        var views = new List<object>();
        object? shell = null, windows = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            windows = ((dynamic)shell!).Windows();
            for (int index = 0; index < (int)((dynamic)windows).Count; index++)
            {
                object? item = null, browserObject = null, document = null, folder = null, self = null;
                IExplorerShellView? view = null;
                try
                {
                    item = ((dynamic)windows).Item(index);
                    if ((nint)(long)((dynamic)item).HWND != window) continue;
                    document = ((dynamic)item).Document; folder = ((dynamic)document).Folder; self = ((dynamic)folder).Self;
                    string path = ((dynamic)self).Path;
                    nint viewWindow = 0;
                    if (item is IExplorerServiceProvider provider && provider.QueryService(new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"), typeof(IExplorerShellBrowser).GUID, out browserObject) >= 0 &&
                        browserObject is IExplorerShellBrowser browser && browser.QueryActiveShellView(out view) >= 0 && view is not null) view.GetWindow(out viewWindow);
                    views.Add(new { Path = path.StartsWith(fixture, StringComparison.OrdinalIgnoreCase) ? path : "virtual-or-unrelated", View = (long)viewWindow,
                        Visible = Native.IsWindowVisible(viewWindow), Root = (long)Native.GetAncestor(viewWindow, 2) });
                }
                finally
                {
                    foreach (object? value in new object?[] { self, folder, document, view, browserObject, item }) if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
                }
            }
        }
        finally { if (windows is not null) Marshal.ReleaseComObject(windows); if (shell is not null) Marshal.ReleaseComObject(shell); }
        Results.Add(new { Name = stage, Status = "INFO", Actual = snapshot.Where(f => f.WindowHandle == window).Select(f => f.Path).ToArray(), Views = views,
            Markers = Native.Children(window).Where(h => Native.Class(h) is "ShellTabWindowClass" or "SHELLDLL_DefView")
                .Select(h => new { Handle = (long)h, Class = Native.Class(h), Visible = Native.IsWindowVisible(h), Parent = (long)Native.GetParent(h) }).ToArray() });
    }

    internal static nint Open(string path)
    {
        // The product owns one Shell STA. Fixture validation runs in a short-lived
        // process so it cannot release an RCW still used by the tested reader.
        var before = Windows();
        nint window = ControlFixture(path);
        if (window == 0 || before.Contains(window) || !Native.IsWindow(window) || Native.Class(window) is not ("CabinetWClass" or "ExploreWClass"))
            throw new Exception("A separate owned Explorer fixture was not verified.");
        owned.Add(window); return window;
    }

    private static void NavigateTabOwned(nint window, string path)
    {
        if (!owned.Contains(window) || !Native.IsWindow(window)) throw new Exception("Not an owned fixture window.");
        if (ControlFixture(path, window) != window) throw new Exception("Fixture tab controller changed its target window.");
    }

    private static nint ControlFixture(string path, nint existingWindow = 0)
    {
        string receipt = Path.Combine(Path.GetTempPath(), "KlasorGecis-fixture-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string argument in new[] { "--open-fixture", "--path", path, "--out", receipt }) start.ArgumentList.Add(argument);
            if (existingWindow != 0) { start.ArgumentList.Add("--window"); start.ArgumentList.Add(((long)existingWindow).ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            using var child = Process.Start(start) ?? throw new Exception("Fixture controller could not start.");
            var elapsed = Stopwatch.StartNew();
            while (!child.WaitForExit(20))
            {
                Application.DoEvents();
                if (elapsed.ElapsedMilliseconds <= 15000) continue;
                child.Kill(); throw new TimeoutException("Owned fixture controller timed out.");
            }
            if (child.ExitCode != 0) throw new Exception("Isolated fixture validation failed: " + File.ReadAllText(receipt));
            using var data = JsonDocument.Parse(File.ReadAllText(receipt));
            return (nint)data.RootElement.GetProperty("Window").GetInt64();
        }
        finally { if (File.Exists(receipt)) File.Delete(receipt); }
    }

    internal static int RunOpenFixture(string[] args)
    {
        string path = args[Array.IndexOf(args, "--path") + 1], output = args[Array.IndexOf(args, "--out") + 1];
        try
        {
            int existing = Array.IndexOf(args, "--window");
            nint window = existing >= 0
                ? NavigateTabIsolated((nint)long.Parse(args[existing + 1], System.Globalization.CultureInfo.InvariantCulture), path)
                : OpenIsolated(path);
            File.WriteAllText(output, JsonSerializer.Serialize(new { Window = (long)window }));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Error = error.GetType().Name, error.Message, Stack = error.StackTrace }));
            return 1;
        }
    }

    private static nint NavigateTabIsolated(nint window, string path)
    {
        if (!Native.IsWindow(window) || Native.Class(window) is not ("CabinetWClass" or "ExploreWClass"))
            throw new Exception("Fixture Explorer window no longer exists.");
        // Set up the source folder through Shell rather than racing Home's
        // asynchronous address initialization after Ctrl+T. COM stays in this
        // short-lived controller, separate from the product reader's RCWs.
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        object? selected = null;
        if (!Until(() =>
        {
            nint activeTab = Native.Children(window).FirstOrDefault(h => Native.Class(h) == "ShellTabWindowClass");
            if (activeTab == 0) return false;
            dynamic windows = ((dynamic)shell).Windows();
            for (int index = 0; index < (int)windows.Count; index++)
            {
                try
                {
                    object item = windows.Item(index);
                    if ((nint)(long)((dynamic)item).HWND != window || item is not IExplorerServiceProvider provider) continue;
                    if (provider.QueryService(new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"), typeof(IExplorerShellBrowser).GUID, out var browserObject) < 0 ||
                        browserObject is not IExplorerShellBrowser browser || browser.QueryActiveShellView(out var view) < 0 || view is null) continue;
                    view.GetWindow(out nint viewWindow);
                    if (!Native.IsChild(activeTab, viewWindow)) continue;
                    selected = item; return true;
                }
                catch (COMException) { }
            }
            return false;
        }, 5000)) throw new Exception("The owned active fixture tab was not ready.");
        ((dynamic)selected!).Navigate2(path);
        if (!Until(() =>
        {
            try { return string.Equals((string)((dynamic)selected!).Document.Folder.Self.Path, path, StringComparison.OrdinalIgnoreCase); }
            catch (COMException) { return false; }
        }, 8000)) throw new Exception("The owned active fixture tab did not navigate to the requested folder.");
        GC.KeepAlive(selected); GC.KeepAlive(shell); return window;
    }

    private static nint OpenIsolated(string path)
    {
        var before = Windows();
        // This short-lived controller uses one apartment and leaves RCW lifetime
        // to CLR teardown. Explore explicitly requests a separate Explorer window.
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        ((dynamic)shell).Explore(path);
        nint found = 0;
        if (!Until(() =>
        {
            dynamic windows = ((dynamic)shell).Windows();
            for (int index = 0; index < (int)windows.Count; index++)
            {
                try
                {
                    dynamic item = windows.Item(index);
                    nint window = (nint)(long)item.HWND;
                    if (before.Contains(window) || !Native.IsWindow(window) || Native.Class(window) is not ("CabinetWClass" or "ExploreWClass")) continue;
                    string actual = (string)item.Document.Folder.Self.Path;
                    if (!string.Equals(actual, path, StringComparison.OrdinalIgnoreCase)) continue;
                    found = window; return true;
                }
                catch (COMException) { }
            }
            return false;
        }, 8000)) throw new Exception("A separate fixture Explorer window could not be verified.");
        GC.KeepAlive(shell);
        owned.Add(found);
        return found;
    }

    private static HashSet<nint> Windows()
    {
        var windows = new HashSet<nint>();
        Native.EnumWindows((h, _) => { windows.Add(h); return true; }, 0);
        return windows;
    }

    private static void NavigateOwned(nint window, string path)
    {
        FocusOwned(window);
        uint thread = Native.GetWindowThreadProcessId(window, out _);
        if (!Native.Keys((0x11, false), (0x4C, false), (0x4C, true), (0x11, true))) throw new Exception("Fixture address shortcut failed.");
        nint edit = 0;
        if (!Until(() => Native.GetForegroundWindow() == window && Native.ModifiersReleased() &&
            Native.Class(edit = Native.Focus(thread)) is "Edit" or "InputSiteWindowClass" && Native.IsChild(window, edit), 1500))
            throw new Exception("Owned fixture address Edit was not verified: " + Native.Class(Native.Focus(thread)));
        // Modern Explorer's address field is a XAML input site. Unicode input
        // follows Ctrl+L in the same keyboard stream; the clipboard is untouched.
        if (Native.Class(edit) == "Edit")
        {
            if (!Native.SetText(edit, path) || !Native.TryReadText(edit, out string actual) || actual != path)
                throw new Exception("Owned fixture address value was not verified.");
        }
        else if (!TypeUnicode(path)) throw new Exception("Fixture Unicode address input failed.");
        if (!Until(() => Native.GetForegroundWindow() == window && Native.ModifiersReleased() &&
            Native.IsChild(window, Native.Focus(thread)) && Native.Class(Native.Focus(thread)) is "Edit" or "InputSiteWindowClass", 1000))
            throw new Exception("Owned fixture address value or focus changed.");
        if (!Native.Keys((0x0D, false), (0x0D, true))) throw new Exception("Fixture navigation input failed.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct UnicodeKey
    { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    { [FieldOffset(0)] public UnicodeKey Key; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Input
    { public uint Type; public InputData Data; }
    private static bool TypeUnicode(string text)
    {
        var inputs = text.SelectMany(character => new[]
        {
            new Input { Type = 1, Data = new InputData { Key = new UnicodeKey { Scan = character, Flags = 4 } } },
            new Input { Type = 1, Data = new InputData { Key = new UnicodeKey { Scan = character, Flags = 6 } } }
        }).ToArray();
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);

    internal static void FocusOwned(nint window)
    {
        if (!owned.Contains(window) || !Native.IsWindow(window)) throw new Exception("Not an owned fixture window.");
        uint self = GetCurrentThreadId();
        uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        uint target = Native.GetWindowThreadProcessId(window, out _);
        bool a = foreground != 0 && foreground != self && AttachThreadInput(self, foreground, true);
        bool b = target != 0 && target != self && target != foreground && AttachThreadInput(self, target, true);
        try { BringWindowToTop(window); SetForegroundWindow(window); }
        finally { if (b) AttachThreadInput(self, target, false); if (a) AttachThreadInput(self, foreground, false); }
        if (Native.GetForegroundWindow() != window) throw new Exception("Fixture could not obtain foreground.");
    }

    internal static void CloseOwned(nint window)
    {
        if (!owned.Contains(window) || !Native.IsWindow(window)) return;
        // Only this run's new top-level Explorer windows are owned; user windows are never closed.
        PostMessage(window, 0x10, 0, 0);
        Until(() => !Native.IsWindow(window), 1500);
    }

    private static bool Until(Func<bool> predicate, int timeout)
    {
        var watch = Stopwatch.StartNew();
        do { if (predicate()) return true; Application.DoEvents(); Thread.Sleep(60); } while (watch.ElapsedMilliseconds < timeout);
        return false;
    }

    private static void Release(object? value)
    {
        if (value is null || !Marshal.IsComObject(value)) return;
        try { Marshal.ReleaseComObject(value); }
        catch (InvalidComObjectException) { }
    }

    private static void Record(string name, bool pass, string? status = null)
    {
        status ??= pass ? "PASS" : "FAIL";
        Results.Add(new { Name = name, Status = status });
        if (status == "FAIL") failures++; if (status == "BLOCKED") blocked++;
        Console.WriteLine(status + " " + name);
    }

    private static int Finish(string output)
    {
        File.WriteAllText(output, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow,
            Version = typeof(ExplorerSource).Assembly.GetName().Version?.ToString(),
            ProductAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ExplorerSource).Assembly.Location))),
            Failures = failures, Blocked = blocked, Results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures > 0 ? 1 : blocked > 0 ? 2 : 0;
    }

    [DllImport("user32.dll")] private static extern bool PostMessage(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint h);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
