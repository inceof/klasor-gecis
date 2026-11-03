using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KlasorGecis;

/// <summary>
/// Reads Explorer's current filesystem views on one message-pumping STA.
/// Concurrent callers share one scan; a stalled Shell never creates more workers.
/// </summary>
internal interface IExplorerSource : IDisposable
{
    Task<IReadOnlyList<FolderLocation>> GetFoldersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FolderLocation>> GetFreshFoldersAsync(CancellationToken cancellationToken);
}

internal sealed class ExplorerSource : IExplorerSource
{
    private static readonly TimeSpan SnapshotWait = TimeSpan.FromSeconds(3);
    private static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserId = typeof(IExplorerShellBrowser).GUID;
    private static readonly Guid DispatchId = new("00020400-0000-0000-C000-000000000046");
    private static readonly IReadOnlyList<FolderLocation> Empty = Array.Empty<FolderLocation>();

    private readonly object gate = new();
    private readonly Thread worker;
    private Control? dispatcher;
    private TaskCompletionSource<IReadOnlyList<FolderLocation>>? request;
    private Task<IReadOnlyList<FolderLocation>>? observedRequest;
    private volatile bool disposed;
    private bool unavailable;
    private long scanCount, scanTicks;
    private int activeScans, maxActiveScans;
    internal ExplorerStatistics Statistics => new(Interlocked.Read(ref scanCount),
        Interlocked.Read(ref scanTicks) * 1000d / Stopwatch.Frequency,
        Volatile.Read(ref maxActiveScans), worker.IsAlive ? 1 : 0);

    public ExplorerSource()
    {
        worker = new Thread(WorkerMain) { IsBackground = true, Name = "Explorer source STA" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public Task<IReadOnlyList<FolderLocation>> GetFoldersAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<FolderLocation>>(cancellationToken);

        Task<IReadOnlyList<FolderLocation>> snapshot;
        lock (gate)
        {
            if (disposed) return Task.FromCanceled<IReadOnlyList<FolderLocation>>(new CancellationToken(true));
            if (unavailable)
                return Task.FromException<IReadOnlyList<FolderLocation>>(new InvalidOperationException(Localizer.T("Explorer okuyucusu kullanılamıyor; programı yeniden başlatın.")));

            // Keep this slot occupied until the actual COM call returns, even if
            // every caller times out or cancels its own wait.
            if (request is null)
            {
                request = new(TaskCreationOptions.RunContinuationsAsynchronously);
                observedRequest = WaitForSnapshotAsync(request.Task);
                if (dispatcher is not null && !PostScan())
                    return observedRequest!;
            }
            snapshot = observedRequest!;
        }
        return cancellationToken.CanBeCanceled ? snapshot.WaitAsync(cancellationToken) : snapshot;
    }

    /// <summary>Returns a scan requested after this call, never a scan already in flight at entry.</summary>
    public async Task<IReadOnlyList<FolderLocation>> GetFreshFoldersAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<FolderLocation>>? previous;
        lock (gate) previous = observedRequest;
        if (previous is not null)
        {
            await previous.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                // A timed-out COM call still owns the worker. Do not accept its old snapshot
                // or queue another worker behind it.
                if (ReferenceEquals(previous, observedRequest)) throw new TimeoutException(Localizer.T("Explorer yanıt vermiyor."));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return await GetFoldersAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<FolderLocation>> WaitForSnapshotAsync(
        Task<IReadOnlyList<FolderLocation>> snapshot)
    {
        // A shared deadline never releases the worker slot. Preserve the timeout
        // as a failure so the UI can distinguish it from no open folders.
        return await snapshot.WaitAsync(SnapshotWait).ConfigureAwait(false);
    }

    private void WorkerMain()
    {
        try
        {
            using var control = new Control();
            _ = control.Handle;
            lock (gate)
            {
                if (disposed)
                    return;
                dispatcher = control;
                if (request is not null && !PostScan())
                    return;
            }
            // Shell COM needs an STA message loop, including between scans.
            Application.Run();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"ExplorerSource worker: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            lock (gate)
            {
                dispatcher = null;
                unavailable = true;
                request?.TrySetException(new InvalidOperationException("Explorer okuyucusu durdu."));
                request = null;
                observedRequest = null;
            }
        }
    }

    // The caller holds gate. There can be only one posted/running scan.
    private bool PostScan()
    {
        try
        {
            dispatcher!.BeginInvoke((Action)ReadRequestedSnapshot);
            return true;
        }
        catch (InvalidOperationException)
        {
            unavailable = true;
            request?.TrySetException(new InvalidOperationException(Localizer.T("Explorer okuyucusuna erişilemiyor.")));
            request = null;
            return false;
        }
    }

    private void ReadRequestedSnapshot()
    {
        TaskCompletionSource<IReadOnlyList<FolderLocation>>? current;
        lock (gate)
            current = request;
        if (current is null)
            return;

        IReadOnlyList<FolderLocation> result = Empty;
        Exception? failure = null;
        long started = Stopwatch.GetTimestamp();
        int active = Interlocked.Increment(ref activeScans);
        maxActiveScans = Math.Max(maxActiveScans, active);
        Interlocked.Increment(ref scanCount);
        try
        {
            if (!disposed)
                result = ReadFolders();
        }
        catch (Exception error)
        {
            failure = error;
            Debug.WriteLine($"ExplorerSource scan: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            Interlocked.Add(ref scanTicks, Stopwatch.GetTimestamp() - started);
            Interlocked.Decrement(ref activeScans);
            lock (gate)
            {
                if (ReferenceEquals(current, request))
                {
                    request = null;
                    observedRequest = null;
                }
            }
            if (failure is not null) current.TrySetException(failure);
            else current.TrySetResult(disposed ? Empty : result);
        }
    }

    private IReadOnlyList<FolderLocation> ReadFolders()
    {
        var elapsed = Stopwatch.StartNew();
        object? shell = null, windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null || (shell = Activator.CreateInstance(shellType)) is null)
                throw new InvalidOperationException(Localizer.T("Windows Shell kullanılamıyor."));
            windows = ((dynamic)shell).Windows();
            if (windows is null)
                throw new InvalidOperationException(Localizer.T("Explorer pencereleri okunamadı."));

            int count = Convert.ToInt32(((dynamic)windows).Count, CultureInfo.InvariantCulture);
            // Do not truncate a tab collection and then assume it was unambiguous.
            if (count is < 0 or > 512)
                throw new InvalidOperationException(Localizer.T("Explorer pencere listesi güvenilir biçimde okunamadı."));

            var candidates = new Dictionary<nint, Candidate>();
            var ambiguous = new HashSet<nint>();
            int failedItems = 0;
            for (int index = 0; index < count; index++)
            {
                if (disposed || elapsed.Elapsed > SnapshotWait)
                    throw new TimeoutException(Localizer.T("Explorer taraması zaman aşımına uğradı."));

                object? item = null;
                try
                {
                    item = ((dynamic)windows).Item(index);
                    if (item is null || ReadCandidate(item) is not { } candidate)
                        continue;
                    nint hwnd = candidate.Location.WindowHandle;
                    if (candidates.TryGetValue(hwnd, out var earlier) && earlier != candidate)
                        ambiguous.Add(hwnd);
                    else
                        candidates[hwnd] = candidate;
                }
                catch (Exception error)
                {
                    failedItems++;
                    // Explorer can close/navigate an item during enumeration.
                    // One disconnected automation object must not hide other windows.
                    Debug.WriteLine($"ExplorerSource item: {error.GetType().Name}: {error.Message}");
                }
                finally
                {
                    ReleaseOwned(item);
                }
            }

            if (disposed || elapsed.Elapsed > SnapshotWait)
                throw new TimeoutException(Localizer.T("Explorer taraması zaman aşımına uğradı."));

            var result = new List<FolderLocation>(candidates.Count);
            foreach (var (hwnd, candidate) in candidates)
            {
                if (!ambiguous.Contains(hwnd)
                    && TryGetExplorerWindow(hwnd, out uint pid) && pid == candidate.Location.ProcessId
                    && TryGetActiveMarker(hwnd, out var marker) && marker == candidate.Marker
                    && MatchesView(hwnd, pid, candidate.ViewWindow, marker))
                    result.Add(candidate.Location);
            }
            if (result.Count == 0 && failedItems != 0)
                throw new InvalidOperationException(Localizer.T("Explorer klasörlerine erişilemedi."));
            return result.ToArray();
        }
        finally
        {
            ReleaseOwned(windows);
            ReleaseOwned(shell);
        }
    }

    private static Candidate? ReadCandidate(object item)
    {
        object rawHandle = ((dynamic)item).HWND;
        nint hwnd = rawHandle is nint pointer
            ? pointer : unchecked((nint)Convert.ToInt64(rawHandle, CultureInfo.InvariantCulture));
        if (!TryGetExplorerWindow(hwnd, out uint pid) || !TryGetActiveMarker(hwnd, out var marker))
            return null;

        object? browserObject = null;
        IExplorerShellView? view = null, confirmedView = null;
        try
        {
            // provider/browser are borrowed interface casts, not new owned RCWs.
            if (item is not IExplorerServiceProvider provider
                || provider.QueryService(TopLevelBrowser, ShellBrowserId, out browserObject) < 0
                || browserObject is not IExplorerShellBrowser browser
                || browser.QueryActiveShellView(out view) < 0 || view is null
                || view.GetWindow(out nint viewWindow) < 0
                || !MatchesView(hwnd, pid, viewWindow, marker)
                || !TryReadViewPath(view, out string path))
                return null;

            // Read the folder from this verified view, never an arbitrary tab's
            // LocationURL. Recheck the view and path across navigation races.
            if (browser.QueryActiveShellView(out confirmedView) < 0 || confirmedView is null
                || !SameComIdentity(view, confirmedView)
                || confirmedView.GetWindow(out nint confirmedWindow) < 0 || confirmedWindow != viewWindow
                || !TryReadViewPath(confirmedView, out string confirmedPath)
                || !string.Equals(path, confirmedPath, StringComparison.Ordinal)
                || !TryGetExplorerWindow(hwnd, out uint currentPid) || currentPid != pid
                || !TryGetActiveMarker(hwnd, out var currentMarker) || currentMarker != marker
                || !MatchesView(hwnd, pid, viewWindow, currentMarker))
                return null;

            string trimmed = path.TrimEnd('\\');
            string displayName = System.IO.Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(displayName) || trimmed.EndsWith(':'))
                displayName = path;
            return new(new FolderLocation(hwnd, pid, path, displayName), viewWindow, marker);
        }
        finally
        {
            // Each returned COM reference is released once on its owning STA.
            // No FinalReleaseComObject, and no extra release for interface aliases.
            ReleaseOwned(confirmedView);
            ReleaseOwned(view);
            ReleaseOwned(browserObject);
        }
    }

    private static bool TryReadViewPath(IExplorerShellView view, out string path)
    {
        path = "";
        object? document = null, folder = null, self = null;
        try
        {
            // SVGIO_BACKGROUND yields this Shell view's automation document.
            // https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellview-getitemobject
            if (view.GetItemObject(0, DispatchId, out document) < 0 || document is null)
                return false;
            folder = ((dynamic)document).Folder;
            if (folder is null)
                return false;
            self = ((dynamic)folder).Self;
            return self is not null && CoreRules.TryNormalizePath((string?)((dynamic)self).Path, out path);
        }
        finally
        {
            ReleaseOwned(self);
            ReleaseOwned(folder);
            ReleaseOwned(document);
        }
    }

    private static bool TryGetExplorerWindow(nint hwnd, out uint pid)
    {
        pid = 0;
        if (hwnd == 0 || !SourceNative.IsWindow(hwnd) || !SourceNative.IsWindowVisible(hwnd)
            || SourceNative.GetAncestor(hwnd, 2) != hwnd
            || GetClassName(hwnd) is not ("CabinetWClass" or "ExploreWClass")
            || SourceNative.DwmGetWindowAttribute(hwnd, 14, out uint cloaked, sizeof(uint)) < 0
            || cloaked != 0 || SourceNative.GetWindowThreadProcessId(hwnd, out pid) == 0 || pid == 0)
            return false;

        using var process = SourceNative.OpenProcess(0x1000, false, pid);
        if (process.IsInvalid)
            return false;
        var name = new StringBuilder(32768);
        uint size = (uint)name.Capacity;
        return SourceNative.QueryFullProcessImageName(process, 0, name, ref size)
            && string.Equals(System.IO.Path.GetFileName(name.ToString()), "explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetActiveMarker(nint hwnd, out ActiveMarker marker)
    {
        // Windows 11 keeps inactive tab HWNDs visible. The selected ShellTabWindowClass
        // is first in sibling z-order; scope view discovery to that tab, then recheck
        // the marker before returning a snapshot. This Explorer detail is version-dependent.
        nint visibleTab = SourceNative.FindWindowEx(hwnd, 0, "ShellTabWindowClass", null);
        nint visibleView = 0;
        bool ambiguous = false;
        int descendants = 0;
        if (visibleTab != 0 && !SourceNative.IsWindowVisible(visibleTab))
        {
            marker = default;
            return false;
        }
        SourceNative.EnumChildWindows(visibleTab != 0 ? visibleTab : hwnd, (child, _) =>
        {
            if (++descendants > 4096)
            {
                ambiguous = true;
                return false;
            }
            if (!SourceNative.IsWindowVisible(child))
                return true;
            switch (GetClassName(child))
            {
                case "SHELLDLL_DefView":
                    if (visibleView != 0 && visibleView != child)
                        ambiguous = true;
                    visibleView = child;
                    break;
                case "ShellTabWindowClass":
                    if (visibleTab != 0 && visibleTab != child)
                        ambiguous = true;
                    visibleTab = child;
                    break;
            }
            return !ambiguous;
        }, 0);
        marker = new(visibleView, visibleTab);
        return !ambiguous && (visibleView != 0 || visibleTab != 0);
    }

    private static bool MatchesView(nint root, uint pid, nint view, ActiveMarker marker)
    {
        if (view == 0 || !SourceNative.IsWindow(view) || !SourceNative.IsWindowVisible(view)
            || !SourceNative.IsChild(root, view) || SourceNative.GetAncestor(view, 2) != root
            || SourceNative.GetWindowThreadProcessId(view, out uint viewPid) == 0 || viewPid != pid)
            return false;
        if (marker.Tab != 0 && view != marker.Tab && !SourceNative.IsChild(marker.Tab, view))
            return false;
        return marker.View == 0 || view == marker.View
            || SourceNative.IsChild(view, marker.View) || SourceNative.IsChild(marker.View, view);
    }

    private static string GetClassName(nint hwnd)
    {
        var name = new StringBuilder(256);
        return SourceNative.GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private static bool SameComIdentity(object left, object right)
    {
        nint first = 0, second = 0;
        try
        {
            first = Marshal.GetIUnknownForObject(left);
            second = Marshal.GetIUnknownForObject(right);
            return first == second;
        }
        finally
        {
            if (second != 0) Marshal.Release(second);
            if (first != 0) Marshal.Release(first);
        }
    }

    private static void ReleaseOwned(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
            return;
        try { Marshal.ReleaseComObject(value); }
        catch (InvalidComObjectException) { }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            request?.TrySetResult(Empty);
            try { dispatcher?.BeginInvoke((Action)Application.ExitThread); }
            catch (InvalidOperationException) { }
        }
        // Never Join/abort the worker or release its COM objects from another
        // apartment. A blocked background STA cannot hold up application exit.
    }

    private readonly record struct ActiveMarker(nint View, nint Tab);
    private sealed record Candidate(FolderLocation Location, nint ViewWindow, ActiveMarker Marker);

    private static class SourceNative
    {
        internal delegate bool EnumWindowProc(nint hwnd, nint lParam);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint hwnd);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsChild(nint parent, nint child);
        [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(nint hwnd, StringBuilder className, int capacity);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumChildWindows(nint parent, EnumWindowProc callback, nint lParam);
        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out uint value, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(uint access,
            [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(SafeProcessHandle process,
            uint flags, StringBuilder name, ref uint size);
    }
}

internal readonly record struct ExplorerStatistics(long ScanCount, double ScanMilliseconds,
    int MaximumConcurrentScans, int WorkerCount);
