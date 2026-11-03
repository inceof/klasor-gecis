using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using KlasorGecis;

internal static class AppearanceHarness
{
    private static readonly List<object> samples = [];
    private static int failures, blocked;

    internal static int Run(string[] args)
    {
        string Option(string name) => args[Array.IndexOf(args, name) + 1];
        string output = Path.GetFullPath(Option("--out")), exe = Path.GetFullPath(Option("--app"));
        int repetitionIndex = Array.IndexOf(args, "--repetitions");
        int repetitions = repetitionIndex >= 0 ? int.Parse(args[repetitionIndex + 1]) : 1;
        string root = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(output)!, "AppearanceFixtures", Guid.NewGuid().ToString("N"))).FullName;
        var windows = new List<nint>(); Process? product = null; uint thread = 0;
        if (Process.GetProcessesByName("KlasorGecis").Length != 0) return 2;
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false };
            if (!args.Contains("--legacy")) { info.ArgumentList.Add("--profile-dir"); info.ArgumentList.Add(Path.Combine(root, "profile")); }
            product = Process.Start(info) ?? throw new Exception("Product did not start.");
            foreach (int count in new[] { 1, 10 })
            {
                while (windows.Count < count)
                    windows.Add(SourceHarness.Open(Directory.CreateDirectory(Path.Combine(root, "Görünüm Kaynak " + windows.Count)).FullName));
                SourceHarness.FocusOwned(windows[0]); Pump(400);
                for (int sample = 0; sample < 10 * repetitions; sample++)
                {
                    using var owner = new Form { Text = "KlasorGecis appearance fixture", Size = new Size(300, 150) };
                    owner.Show(); LiveHarness.FocusTestWindow(owner.Handle);
                    using FileDialog dialog = sample % 2 == 0 ? new OpenFileDialog() : new SaveFileDialog();
                    dialog.Title = "KlasorGecis appearance test";
                    dialog.InitialDirectory = Directory.CreateDirectory(Path.Combine(root, "Initial")).FullName;
                    dialog.FileName = "synthetic-Çınar.txt";
                    using var timer = new System.Windows.Forms.Timer { Interval = 10 };
                    var watch = Stopwatch.StartNew(); double? seen = null; bool finished = false;
                    timer.Tick += (_, _) =>
                    {
                        if (finished) return;
                        nint target = LiveHarness.FindOwnDialog(dialog.Title);
                        if (target != 0) seen ??= watch.Elapsed.TotalMilliseconds;
                        nint bar = 0;
                        Native.EnumWindows((window, _) =>
                        {
                            Native.GetWindowThreadProcessId(window, out uint pid);
                            if (pid == product.Id && Native.Title(window) == "Klasör Geçiş" && Native.IsWindowVisible(window) && GetWindow(window, 4) == target &&
                                Native.Children(window).Any(child => Native.IsWindowVisible(child) && Native.IsWindowEnabled(child) && Native.Class(child).Contains("BUTTON", StringComparison.OrdinalIgnoreCase)))
                            { bar = window; return false; }
                            return true;
                        }, 0);
                        if (seen is not null && bar != 0 && Native.GetForegroundWindow() == target)
                        {
                            thread = Native.GetWindowThreadProcessId(bar, out _);
                            samples.Add(new { Status = "PASS", SourcesCreated = count, Repetition = sample / 10 + 1, Sample = sample % 10 + 1,
                                Dialog = sample % 2 == 0 ? "Open" : "Save", RowFromObservedDialogMs = watch.Elapsed.TotalMilliseconds - seen.Value });
                            finished = true; timer.Stop(); PostMessage(target, 0x10, 0, 0);
                        }
                        else if (watch.Elapsed.TotalSeconds > 6)
                        {
                            bool lost = target != 0 && Native.GetForegroundWindow() != target;
                            samples.Add(new { Status = lost ? "BLOCKED" : "FAIL", SourcesCreated = count, Sample = sample + 1 });
                            if (lost) blocked++; else failures++;
                            finished = true; timer.Stop();
                            if (target != 0) PostMessage(target, 0x10, 0, 0); else owner.Close();
                        }
                    };
                    timer.Start(); dialog.ShowDialog(owner); timer.Stop();
                }
            }
        }
        catch (Exception e) { failures++; samples.Add(new { Status = "FAIL", Error = e.GetType().Name, Stack = e.StackTrace }); }
        finally
        {
            if (product is not null)
            {
                if (!product.HasExited && thread != 0) PostThreadMessage(thread, 0x12, 0, 0);
                if (!product.WaitForExit(2500)) product.Kill();
                product.Dispose();
            }
            foreach (nint window in windows) SourceHarness.CloseOwned(window);
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Utc = DateTimeOffset.UtcNow, ExeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe))),
            Repetitions = repetitions,
            Method = "10 ms observations: first native dialog HWND observation to visible attached row with native button; Open/Save alternated",
            Failures = failures, Blocked = blocked, Samples = samples
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failures > 0 ? 1 : blocked > 0 ? 2 : 0;
    }

    private static void Pump(int milliseconds) { var watch = Stopwatch.StartNew(); do { Application.DoEvents(); Thread.Sleep(10); } while (watch.ElapsedMilliseconds < milliseconds); }
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint relation);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nint w, nint l);
}
