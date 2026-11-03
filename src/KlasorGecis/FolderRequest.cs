namespace KlasorGecis;

internal enum FolderOrigin { OpenExplorer, Favorite, Recent }
internal sealed record FolderRequest(FolderOrigin Origin, string Path, FolderLocation? ExplorerSource = null)
{
    internal static FolderRequest Explorer(FolderLocation source) => new(FolderOrigin.OpenExplorer, source.Path, source);
    internal static FolderRequest? Saved(FolderOrigin origin, string path) => origin is FolderOrigin.Favorite or FolderOrigin.Recent &&
        CoreRules.TryNormalizePath(path, out var normalized) ? new(origin, normalized) : null;
}
