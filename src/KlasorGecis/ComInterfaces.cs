using System.Runtime.InteropServices;

namespace KlasorGecis;

// The declarations keep the native IOleWindow / IShellBrowser / IShellView
// vtable order. Only the read-only methods are called by ExplorerSource.
[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerServiceProvider
{
    [PreserveSig]
    int QueryService(in Guid service, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object? result);
}

[ComImport, Guid("000214E2-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerShellBrowser
{
    [PreserveSig] int GetWindow(out nint window);
    [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    [PreserveSig] int InsertMenusSB(nint menu, nint menuWidths);
    [PreserveSig] int SetMenuSB(nint menu, nint oleMenu, nint activeObjectWindow);
    [PreserveSig] int RemoveMenusSB(nint menu);
    [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string? text);
    [PreserveSig] int EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
    [PreserveSig] int TranslateAcceleratorSB(nint message, ushort id);
    [PreserveSig] int BrowseObject(nint pidl, uint flags);
    [PreserveSig] int GetViewStateStream(uint mode, out nint stream);
    [PreserveSig] int GetControlWindow(uint id, out nint window);
    [PreserveSig] int SendControlMsg(uint id, uint message, nuint wParam,
        nint lParam, out nint result);
    [PreserveSig] int QueryActiveShellView(out IExplorerShellView? view);
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerShellView
{
    [PreserveSig] int GetWindow(out nint window);
    [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    [PreserveSig] int TranslateAccelerator(nint message);
    [PreserveSig] int EnableModeless([MarshalAs(UnmanagedType.Bool)] bool enable);
    [PreserveSig] int UIActivate(uint state);
    [PreserveSig] int Refresh();
    [PreserveSig] int CreateViewWindow(nint previousView, nint settings,
        nint browser, nint bounds, out nint window);
    [PreserveSig] int DestroyViewWindow();
    [PreserveSig] int GetCurrentInfo(nint settings);
    [PreserveSig] int AddPropertySheetPages(uint reserved, nint callback, nint lParam);
    [PreserveSig] int SaveViewState();
    [PreserveSig] int SelectItem(nint pidl, uint flags);
    [PreserveSig] int GetItemObject(uint item, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object? result);
}
