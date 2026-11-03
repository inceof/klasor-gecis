namespace KlasorGecis;

internal sealed record DialogTarget(nint Handle, uint ProcessId, uint ThreadId, long ProcessCreated,
    nint AddressRoot, nint FilenameEdit, nint FilterCombo)
{
    internal static DialogTarget? Detect(nint handle)
    {
        if (handle == 0 || !Native.IsWindowVisible(handle) || !Native.IsWindowEnabled(handle) ||
            Native.IsIconic(handle) || Native.Class(handle) != "#32770") return null;
        var children = Native.Children(handle);
        if (!children.Any(h => Native.Class(h) == "SHELLDLL_DefView")) return null;
        var address = children.FirstOrDefault(h => Native.Class(h) == "Address Band Root");
        if (address == 0) return null;
        var filename = children.FirstOrDefault(h => Native.Class(h) == "Edit" &&
            Native.GetDlgCtrlID(h) == 1148 && !Native.IsChild(address, h));
        if (filename == 0)
            filename = children.FirstOrDefault(h => Native.Class(h) == "Edit" &&
                Native.GetDlgCtrlID(h) == 1001 && !Native.IsChild(address, h) &&
                Native.Class(Native.GetParent(h)) == "ComboBox");
        var combo = children.FirstOrDefault(h => Native.Class(h) == "ComboBox" && Native.GetDlgCtrlID(h) == 1136);
        if (combo == 0)
        {
            // Modern Save dialogs may assign zero instead of cmb1. Only accept a
            // unique, visible, non-editable selector outside the address/filename area.
            var selectors = children.Where(h => Native.Class(h) == "ComboBox" && Native.IsWindowVisible(h) &&
                !Native.IsChild(address, h) && (filename == 0 || !Native.IsChild(h, filename)) &&
                !Native.Children(h).Any(c => Native.Class(c) == "Edit")).ToArray();
            if (selectors.Length == 1) combo = selectors[0];
        }
        var thread = Native.GetWindowThreadProcessId(handle, out var pid);
        // Elevated dialogs are also detected. Windows UIPI may independently reject
        // navigation; no elevation request or privileged helper is ever started.
        var created = Native.ProcessCreation(pid);
        return created == 0 ? null : new(handle, pid, thread, created, address, filename, combo);
    }

    internal bool IsCurrent(bool requireForeground = true)
    {
        if (!Native.IsWindow(Handle) || !Native.IsWindowVisible(Handle) || !Native.IsWindowEnabled(Handle) ||
            Native.IsIconic(Handle) || Native.Class(Handle) != "#32770") return false;
        if (requireForeground && Native.GetForegroundWindow() != Handle) return false;
        var thread = Native.GetWindowThreadProcessId(Handle, out var pid);
        return thread == ThreadId && pid == ProcessId && Native.ProcessCreation(pid) == ProcessCreated &&
               Native.IsChild(Handle, AddressRoot) && Native.Class(AddressRoot) == "Address Band Root";
    }

    internal bool IsAddressEdit(nint h) => h != 0 && h != FilenameEdit && Native.IsChild(AddressRoot, h) &&
        Native.Class(h) == "Edit" && Native.IsWindowVisible(h) && Native.IsWindowEnabled(h);

    internal string? CurrentFolder()
    {
        foreach (var h in Native.Children(AddressRoot))
        {
            if (Native.Class(h) != "ToolbarWindow32") continue;
            var title = Native.Title(h);
            // The localized prefix may be "Adres:", "Address:" etc.; validate the remaining absolute path.
            if (CoreRules.TryNormalizePath(title, out var raw)) return raw;
            var split = title.IndexOf(": ", StringComparison.Ordinal);
            if (split >= 0 && CoreRules.TryNormalizePath(title[(split + 2)..], out var p)) return p;
        }
        return null;
    }

    internal int? FilterIndex()
    {
        if (FilterCombo == 0) return null;
        return Native.SendMessageTimeout(FilterCombo, 0x147, 0, 0, 2, 250, out var r) != 0 ? (int)r : null;
    }
}
