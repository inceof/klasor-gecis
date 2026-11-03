using System.Collections.Concurrent;

namespace KlasorGecis;

internal enum FolderAvailabilityStatus { Directory, NotDirectory, Unavailable, TimedOut }

internal sealed class FolderAvailability : IDisposable
{
    private sealed record Request(string Path, TaskCompletionSource<FolderAvailabilityStatus> Completion);
    private readonly object gate = new();
    private readonly BlockingCollection<Request> work = new(1);
    private readonly Func<string, FolderAvailabilityStatus> probe;
    private Request? pending;
    private Task<FolderAvailabilityStatus>? observed;
    private Thread? worker;
    private volatile bool disposed;
    private int active, maximum;
    internal int MaximumConcurrentChecks => Volatile.Read(ref maximum);

    internal FolderAvailability() : this(ReadDirectory) { }
    internal FolderAvailability(Func<string, FolderAvailabilityStatus> probe) { this.probe = probe; }

    internal async Task<FolderAvailabilityStatus> CheckAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!CoreRules.TryNormalizePath(path, out var normalized)) return FolderAvailabilityStatus.NotDirectory;
        Task<FolderAvailabilityStatus>? previous;
        lock (gate) previous = pending is not null && pending.Path != normalized ? observed : null;
        if (previous is not null) await previous.WaitAsync(token).ConfigureAwait(false);
        Task<FolderAvailabilityStatus> task;
        lock (gate)
        {
            if (disposed) return FolderAvailabilityStatus.Unavailable;
            if (pending is not null && !string.Equals(pending.Path, normalized, StringComparison.OrdinalIgnoreCase))
                return FolderAvailabilityStatus.Unavailable;
            if (pending is null)
            {
                var request = new Request(normalized, new(TaskCreationOptions.RunContinuationsAsynchronously));
                pending = request; observed = Observe(request.Completion.Task);
                worker ??= StartWorker();
                work.Add(request);
            }
            task = observed!;
        }
        return await task.WaitAsync(token).ConfigureAwait(false);
    }

    private Thread StartWorker()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Folder availability" };
        thread.Start(); return thread;
    }

    private void Run()
    {
        try
        {
            foreach (var request in work.GetConsumingEnumerable())
            {
                var result = FolderAvailabilityStatus.Unavailable;
                int count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count);
                try { if (!disposed) result = probe(request.Path); }
                catch (Exception) { result = FolderAvailabilityStatus.Unavailable; }
                finally
                {
                    Interlocked.Decrement(ref active);
                    lock (gate) { if (ReferenceEquals(pending, request)) { pending = null; observed = null; } }
                    request.Completion.TrySetResult(result);
                }
            }
        }
        finally { work.Dispose(); }
    }

    private static async Task<FolderAvailabilityStatus> Observe(Task<FolderAvailabilityStatus> request)
    {
        try { return await request.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { return FolderAvailabilityStatus.TimedOut; }
    }

    private static FolderAvailabilityStatus ReadDirectory(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Directory) != 0 ? FolderAvailabilityStatus.Directory : FolderAvailabilityStatus.NotDirectory; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return FolderAvailabilityStatus.NotDirectory; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return FolderAvailabilityStatus.Unavailable; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; pending?.Completion.TrySetResult(FolderAvailabilityStatus.Unavailable);
            work.CompleteAdding();
            if (worker is null) work.Dispose();
        }
        // A blocked filesystem call remains on its one background worker, never aborted/joined.
    }
}
