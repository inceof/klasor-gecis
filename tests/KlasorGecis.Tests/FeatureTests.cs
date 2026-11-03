using System.Reflection;
using System.Runtime.InteropServices;
using KlasorGecis;

internal static class FeatureTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        check("queued source update survives native folder bar destruction", () => InSta(() => WithSettings(path =>
        {
            using var source = new CountingEmptySource();
            using var context = new FolderAppContext(path, source);
            ((System.Windows.Forms.Timer)typeof(FolderAppContext).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!).Stop();
            var bar = (Form)typeof(FolderAppContext).GetField("_bar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
            nint old = bar.Handle;
            if (!DestroyWindow(old) || Native.IsWindow(old)) return false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (source.Reads == 0 && clock.ElapsedMilliseconds < 500) { Application.DoEvents(); Thread.Sleep(10); }
            return source.Reads > 0;
        })));
        var a = new FolderLocation(11, 101, @"D:\Kaynak A", "Kaynak A");
        var b = new FolderLocation(22, 202, @"D:\Kaynak B", "Kaynak B");
        var recent = new Dictionary<nint, long> { [11] = 8, [22] = 12 };
        check("source order prefers most recently used valid window", () =>
            SourceSelection.Order(new[] { a, b }, recent, _ => true)[0] == b);
        check("closed or identity-changed sources are excluded", () =>
            SourceSelection.Order(new[] { a, b }, recent, f => f.ProcessId == 101).Single() == a);
        check("explicit menu selection cannot fall back to a different source", () =>
            SourceSelection.Resolve(a, new[] { b }) is null);
        check("menu selection rejects changed path in same Explorer window", () =>
            SourceSelection.Resolve(a, new[] { a with { Path = @"D:\Yeni klasör" } }) is null);
        check("menu selection rejects reused window from another process", () =>
            SourceSelection.Resolve(a, new[] { a with { ProcessId = 303 } }) is null);
        check("menu selection accepts fresh matching source", () =>
            SourceSelection.Resolve(a, new[] { a, b }) == a);
        check("shortcut parser accepts Ctrl Shift K", () =>
            ShortcutBinding.FromKeys(Keys.Control | Keys.Shift | Keys.K)?.DisplayText == "Ctrl+Shift+K");
        check("shortcut parser rejects unmodified letters", () =>
            ShortcutBinding.FromKeys(Keys.G) is null);
        check("shortcut parser rejects reserved Alt Tab", () =>
            ShortcutBinding.FromKeys(Keys.Alt | Keys.Tab) is null);
        check("Explorer timeout is distinct from no open folders", () =>
        {
            var pending = new TaskCompletionSource<IReadOnlyList<FolderLocation>>();
            var method = typeof(ExplorerSource).GetMethod("WaitForSnapshotAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            var task = (Task<IReadOnlyList<FolderLocation>>)method.Invoke(null, new object[] { pending.Task })!;
            try { task.GetAwaiter().GetResult(); return false; }
            catch (TimeoutException) { return true; }
        });
        check("empty Explorer snapshot remains a successful empty list", () =>
        {
            var method = typeof(ExplorerSource).GetMethod("WaitForSnapshotAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            var task = (Task<IReadOnlyList<FolderLocation>>)method.Invoke(null,
                new object[] { Task.FromResult<IReadOnlyList<FolderLocation>>(Array.Empty<FolderLocation>()) })!;
            return task.GetAwaiter().GetResult().Count == 0;
        });
        check("source MRU changes immediately and closed source cannot remain preferred", () =>
        {
            recent[11] = 20;
            return SourceSelection.Order(new[] { a, b }, recent, _ => true)[0] == a &&
                SourceSelection.Order(new[] { a, b }, recent, f => f != a)[0] == b;
        });
        check("shortcut settings round trip in Turkish spaced directory", () => WithSettings(path =>
        {
            var binding = ShortcutBinding.FromKeys(Keys.Control | Keys.Shift | Keys.K)!;
            return ShortcutSettings.Save(path, binding, out _) && ShortcutSettings.Load(path, out var error) == binding && error is null;
        }));
        check("corrupt shortcut file is reported and never overwritten on load", () => WithSettings(path =>
        {
            File.WriteAllText(path, "{broken-json");
            return ShortcutSettings.Load(path, out var error) == ShortcutBinding.Default && error is not null &&
                File.ReadAllText(path) == "{broken-json";
        }));
        check("invalid stored modifiers are rejected without overwriting settings", () => WithSettings(path =>
        {
            File.WriteAllText(path, "{\"Modifiers\":8,\"Key\":71}");
            return ShortcutSettings.Load(path, out var error) == ShortcutBinding.Default && error is not null &&
                File.ReadAllText(path) == "{\"Modifiers\":8,\"Key\":71}";
        }));
        check("shortcut conflict retains registered old key and saved value", () => InSta(() => WithSettings(path =>
        {
            using var first = new Form(); using var other = new Form();
            var old = ShortcutBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F21)!;
            var candidate = ShortcutBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F22)!;
            using var registration = new ShortcutRegistration(first.Handle, old);
            using var conflict = new ShortcutRegistration(other.Handle, candidate);
            if (!registration.Register() || !conflict.Register()) throw new Exception("Synthetic test shortcut is already occupied.");
            ShortcutSettings.Save(path, old, out _);
            return registration.Apply(candidate, path) is not null && registration.Active && registration.Binding == old &&
                ShortcutSettings.Load(path, out _) == old;
        })));
        check("failed shortcut write retains old registration and releases candidate", () => InSta(() => WithSettings(path =>
        {
            using var first = new Form(); using var other = new Form();
            var old = ShortcutBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F21)!;
            var candidate = ShortcutBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F22)!;
            using var registration = new ShortcutRegistration(first.Handle, old);
            using var probe = new ShortcutRegistration(other.Handle, candidate);
            if (!registration.Register()) throw new Exception("Synthetic test shortcut is already occupied.");
            bool rejected = registration.Apply(candidate, Path.GetDirectoryName(path)!) is not null;
            return rejected && registration.Active && registration.Binding == old && probe.Register();
        })));
        check("successful shortcut replacement frees old key and persists new key", () => InSta(() => WithSettings(path =>
        {
            using var first = new Form(); using var other = new Form();
            var old = ShortcutBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F21)!;
            var candidate = ShortcutBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F22)!;
            using var registration = new ShortcutRegistration(first.Handle, old);
            using var probe = new ShortcutRegistration(other.Handle, old);
            if (!registration.Register()) throw new Exception("Synthetic test shortcut is already occupied.");
            return registration.Apply(candidate, path) is null && registration.Active && registration.Binding == candidate &&
                ShortcutSettings.Load(path, out _) == candidate && probe.Register();
        })));
        check("Explorer reader failure is shown in tray instead of empty-folder status", () => InSta(() => WithSettings(path =>
        {
            using var context = new FolderAppContext(path);
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var reader = (ExplorerSource)typeof(FolderAppContext).GetField("_source", fields)!.GetValue(context)!;
            // Controlled COM-reader failure; no real Explorer process is stalled or killed.
            typeof(ExplorerSource).GetField("unavailable", fields)!.SetValue(reader, true);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var sourceMenu = (ToolStripMenuItem)typeof(FolderAppContext).GetField("_sourceMenu", fields)!.GetValue(context)!;
            do
            {
                Application.DoEvents();
                if (sourceMenu.Text?.Contains("okunamadı", StringComparison.Ordinal) == true) return true;
                Thread.Sleep(10);
            } while (watch.ElapsedMilliseconds < 1500);
            return false;
        })));
    }

    private sealed class CountingEmptySource : IExplorerSource
    {
        internal int Reads;
        public Task<IReadOnlyList<FolderLocation>> GetFoldersAsync(CancellationToken token) { Reads++; return Task.FromResult<IReadOnlyList<FolderLocation>>([]); }
        public Task<IReadOnlyList<FolderLocation>> GetFreshFoldersAsync(CancellationToken token) => GetFoldersAsync(token);
        public void Dispose() { }
    }
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);

    private static bool WithSettings(Func<string, bool> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "KlasorGecis-test-Çınar " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { return test(Path.Combine(directory, "shortcut.json")); }
        finally { Directory.Delete(directory, true); }
    }

    private static bool InSta(Func<bool> test)
    {
        bool result = false; Exception? error = null;
        var thread = new Thread(() => { try { result = test(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) throw error;
        return result;
    }
}
