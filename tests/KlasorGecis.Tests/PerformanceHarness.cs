using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using KlasorGecis;

internal static class PerformanceHarness
{
    private static readonly List<object> samples = [];
    private static string output = "";
    private static int failures, blocked;
    private static readonly int[] sourceCounts = [1, 10];

    internal static int Run(string[] args)
    {
        string Option(string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 ? args[i + 1] : fallback; }
        output = Path.GetFullPath(Option("--out", "performance.json"));
        int seconds = int.Parse(Option("--seconds", "60")), repetitions = int.Parse(Option("--repetitions", "3"));
        string root = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(output)!, "PerformanceFixtures", Guid.NewGuid().ToString("N"))).FullName;
        if (!ProfilePaths.Select(new(root, true, false), out var profileError)) throw new Exception(profileError);
        var windows = new List<nint>();
        try
        {
            foreach (int count in sourceCounts)
            {
                while (windows.Count < count)
                    windows.Add(SourceHarness.Open(Directory.CreateDirectory(Path.Combine(root, "Kaynak Çınar " + windows.Count)).FullName));
                SourceHarness.FocusOwned(windows[0]);
                using var context = new FolderAppContext(Path.Combine(root, "shortcut.json"));
                var source = (ExplorerSource)typeof(FolderAppContext).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
                using var owner = new Form { Text = "KlasorGecis controlled performance", Size = new Size(320, 180), StartPosition = FormStartPosition.CenterScreen };
                owner.Show();
                for (int repetition = 0; repetition < repetitions; repetition++)
                {
                    LiveHarness.FocusTestWindow(owner.Handle);
                    Pump(700);
                    var idle = Measure(context, source, owner.Handle, count, repetition, "idle", seconds, null);
                    while (!idle.IsCompleted) Pump(100);
                    idle.GetAwaiter().GetResult();
                    using FileDialog dialog = repetition % 2 == 0 ? new OpenFileDialog() : new SaveFileDialog();
                    string initial = Directory.CreateDirectory(Path.Combine(root, "Başlangıç")).FullName;
                    string destination = Directory.CreateDirectory(Path.Combine(root, "Hedef %25")).FullName;
                    dialog.Title = "KlasorGecis performance dialog";
                    dialog.InitialDirectory = initial; dialog.FileName = "sentetik-Çınar.txt";
                    dialog.Filter = "Text|*.txt|All|*.*"; dialog.FilterIndex = 2;
                    using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                    bool started = false;
                    timer.Tick += async (_, _) =>
                    {
                        if (started) return;
                        nint window = LiveHarness.FindOwnDialog(dialog.Title);
                        if (window == 0) return;
                        started = true; timer.Stop();
                        try
                        {
                            LiveHarness.FocusTestWindow(window);
                            await Task.Delay(500);
                            var target = DialogTarget.Detect(window) ?? throw new Exception("Controlled dialog not detected.");
                            await Measure(context, source, window, count, repetition, repetition % 2 == 0 ? "open" : "save", seconds,
                                async index => await DialogNavigator.NavigateAsync(target, index % 2 == 0 ? destination : initial, CancellationToken.None));
                        }
                        catch (Exception e) { failures++; samples.Add(new { Status = "FAIL", Error = e.GetType().Name }); Save(); }
                        finally { if (Native.IsWindow(window)) PostMessage(window, 0x10, 0, 0); }
                    };
                    timer.Start(); dialog.ShowDialog(owner); timer.Stop();
                }
            }
        }
        catch (Exception e) { failures++; samples.Add(new { Status = "FAIL", Error = e.GetType().Name, e.Message, Stack = e.StackTrace }); }
        finally { foreach (nint window in windows) SourceHarness.CloseOwned(window); Save(); }
        return failures > 0 ? 1 : blocked > 0 ? 2 : 0;
    }

    private static async Task Measure(FolderAppContext context, ExplorerSource source, nint foreground,
        int count, int repetition, string mode, int seconds, Func<int, Task<NavigationResult>>? navigate)
    {
        int observedSources = (await source.GetFreshFoldersAsync(CancellationToken.None)).Count;
        var process = Process.GetCurrentProcess(); process.Refresh();
        TimeSpan cpu = process.TotalProcessorTime;
        long memory = process.PrivateMemorySize64;
        var before = source.Statistics;
        int lost = 0, navCount = 0;
        string? navigationFailure = null;
        var latencies = new List<double>();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (Native.GetForegroundWindow() != foreground) lost++;
            if (navigate is not null && navCount < 10 && clock.Elapsed.TotalSeconds >= navCount * 2)
            {
                if (Native.GetForegroundWindow() != foreground) break;
                var navClock = Stopwatch.StartNew();
                await source.GetFreshFoldersAsync(CancellationToken.None);
                var result = await navigate(navCount);
                if (!result.Success) { navigationFailure = result.Code; break; }
                latencies.Add(navClock.Elapsed.TotalMilliseconds); navCount++;
            }
            if (navigate is null) Pump(100);
            else await Task.Delay(100);
        }
        process.Refresh(); var after = source.Statistics;
        if (lost != 0) blocked++;
        if (navigationFailure is not null) failures++;
        if (observedSources < count) failures++;
        latencies.Sort();
        samples.Add(new
        {
            Status = navigationFailure is not null || observedSources < count ? "FAIL" : lost == 0 ? "PASS" : "BLOCKED", SourcesCreated = count,
            ObservedFileSystemSources = observedSources, ExistingSources = observedSources - count, Repetition = repetition + 1, Mode = mode,
            NavigationFailure = navigationFailure,
            WallSeconds = clock.Elapsed.TotalSeconds, CpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
            PrivateBytesBefore = memory, PrivateBytesAfter = process.PrivateMemorySize64,
            Scans = after.ScanCount - before.ScanCount, ScanMilliseconds = after.ScanMilliseconds - before.ScanMilliseconds,
            after.MaximumConcurrentScans, after.WorkerCount, LostForegroundObservations = lost,
            NavigationSamples = latencies.Count,
            NavigationMilliseconds = latencies.ToArray(),
            NavigationP50Ms = latencies.Count == 0 ? (double?)null : latencies[(int)Math.Ceiling(latencies.Count * .5) - 1],
            NavigationP95Ms = latencies.Count == 0 ? (double?)null : latencies[(int)Math.Ceiling(latencies.Count * .95) - 1]
        });
        Save();
    }

    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(10); } while (clock.ElapsedMilliseconds < milliseconds);
    }

    private static void Save() => File.WriteAllText(output, JsonSerializer.Serialize(new
    {
        Utc = DateTimeOffset.UtcNow, OS = Environment.OSVersion.VersionString, LogicalProcessors = Environment.ProcessorCount,
        Runtime = Environment.Version.ToString(), SettingsProfile = new { AutoSwitchEnabled = false, HistoryEnabled = false, Favorites = 0, Isolated = true },
        AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ExplorerSource).Assembly.Location))),
        HarnessSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PerformanceHarness).Assembly.Location))),
        Failures = failures, Blocked = blocked, Samples = samples
    }, new JsonSerializerOptions { WriteIndented = true }));

    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint w, nint l);
}
