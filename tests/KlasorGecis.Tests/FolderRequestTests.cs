using KlasorGecis;

internal static class FolderRequestTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        check("saved file target is rejected before any dialog input", () => PreferencesTests.WithFile(path =>
        {
            File.WriteAllText(path, "harmless synthetic file");
            using var checker = new FolderAvailability();
            return checker.CheckAsync(path, CancellationToken.None).GetAwaiter().GetResult() == FolderAvailabilityStatus.NotDirectory;
        }));
        check("saved folder does not require a live Explorer identity", () => PreferencesTests.WithFile(path =>
        {
            using var checker = new FolderAvailability();
            var request = FolderRequest.Saved(FolderOrigin.Favorite, Path.GetDirectoryName(path)!);
            return request is not null && request.ExplorerSource is null &&
                checker.CheckAsync(request.Path, CancellationToken.None).GetAwaiter().GetResult() == FolderAvailabilityStatus.Directory &&
                FolderRequest.Saved(FolderOrigin.Favorite, "shell:Downloads") is null;
        }));
        check("timed out filesystem call keeps its one worker slot occupied", () =>
        {
            using var release = new ManualResetEventSlim();
            int calls = 0;
            using var checker = new FolderAvailability(_ => { Interlocked.Increment(ref calls); release.Wait(); return FolderAvailabilityStatus.Directory; });
            try
            {
                var first = checker.CheckAsync(@"D:\One", CancellationToken.None).GetAwaiter().GetResult();
                var second = checker.CheckAsync(@"D:\Two", CancellationToken.None).GetAwaiter().GetResult();
                return first == FolderAvailabilityStatus.TimedOut && second == FolderAvailabilityStatus.Unavailable && calls == 1 && checker.MaximumConcurrentChecks == 1;
            }
            finally { release.Set(); }
        });
        check("canceled availability request does not probe the filesystem", () =>
        {
            int calls = 0;
            using var checker = new FolderAvailability(_ => { calls++; return FolderAvailabilityStatus.Directory; });
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { checker.CheckAsync(@"D:\One", cancellation.Token).GetAwaiter().GetResult(); return false; }
            catch (OperationCanceledException) { return calls == 0; }
        });
        check("removed saved directory is rejected and drive root remains a directory", () => PreferencesTests.WithFile(path =>
        {
            using var checker = new FolderAvailability();
            return checker.CheckAsync(Path.Combine(Path.GetDirectoryName(path)!, "missing"), CancellationToken.None).GetAwaiter().GetResult() == FolderAvailabilityStatus.NotDirectory &&
                checker.CheckAsync(Path.GetPathRoot(path)!, CancellationToken.None).GetAwaiter().GetResult() == FolderAvailabilityStatus.Directory;
        }));
        check("filesystem access errors are visible and do not leave a worker occupied", () =>
        {
            using var checker = new FolderAvailability(_ => throw new UnauthorizedAccessException());
            return checker.CheckAsync(@"D:\Denied", CancellationToken.None).GetAwaiter().GetResult() == FolderAvailabilityStatus.Unavailable &&
                checker.CheckAsync(@"D:\Other", CancellationToken.None).GetAwaiter().GetResult() == FolderAvailabilityStatus.Unavailable && checker.MaximumConcurrentChecks == 1;
        });
    }
}
