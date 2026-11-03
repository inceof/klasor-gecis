namespace KlasorGecis;

internal sealed record FolderLocation(
    nint WindowHandle,
    uint ProcessId,
    string Path,
    string DisplayName);
