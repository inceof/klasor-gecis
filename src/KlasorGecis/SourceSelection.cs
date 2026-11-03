namespace KlasorGecis;

internal static class SourceSelection
{
    internal static bool IsLive(FolderLocation source) =>
        Native.IsWindow(source.WindowHandle) && Native.IsWindowVisible(source.WindowHandle) &&
        Native.GetWindowThreadProcessId(source.WindowHandle, out var pid) != 0 && pid == source.ProcessId;

    internal static IReadOnlyList<FolderLocation> Order(IEnumerable<FolderLocation> folders,
        IReadOnlyDictionary<nint, long> recency, Func<FolderLocation, bool> isLive) =>
        folders.Where(isLive).OrderByDescending(f => recency.GetValueOrDefault(f.WindowHandle, long.MinValue)).ToArray();

    // A menu choice means this exact folder, never a different tab or an MRU fallback.
    internal static FolderLocation? Resolve(FolderLocation chosen, IEnumerable<FolderLocation> fresh) =>
        fresh.FirstOrDefault(f => f.WindowHandle == chosen.WindowHandle && f.ProcessId == chosen.ProcessId &&
            string.Equals(f.Path, chosen.Path, StringComparison.OrdinalIgnoreCase));
}
