using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace KlasorGecis;

internal static class Native
{
    internal delegate bool EnumProc(nint hwnd, nint parameter);
    internal delegate void WinEventProc(nint hook, uint ev, nint hwnd, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public RECT CaretRect;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO
    {
        public uint Size; public RECT Monitor, Work; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    {
        public int Dx, Dy; public uint Data, Flags, Time; public nuint Extra;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    {
        public ushort Key, Scan; public uint Flags, Time; public nuint Extra;
    }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT
    {
        public uint Type; public INPUTUNION Data;
    }
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint Size, Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    private static extern nint ReadSelectionMessage(nint window, uint message, out uint start, out uint end,
        uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(nint window, uint message, nint w, nint l);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(nint h);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] internal static extern bool IsChild(nint parent, nint h);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint h);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint p);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint h, EnumProc callback, nint p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint h, StringBuilder b, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint h, StringBuilder b, int count);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] internal static extern int GetDlgCtrlID(nint h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint h, out RECT rect);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint h);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint h, uint attribute, out RECT value, int size);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint h, int id);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendTextMessage(nint h, uint m, nint w, string text, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    private static extern nint ReadTextMessage(nint h, uint m, nint w, StringBuilder text, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    internal static extern nint SendMessageTimeout(nint h, uint m, nint w, nint l, uint flags, uint timeout, out nint result);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref uint size);
    internal static string? ProcessPath(uint pid)
    {
        nint process = OpenProcess(0x1000, false, pid);
        if (process == 0) return null;
        try
        {
            var name = new StringBuilder(32768); uint size = (uint)name.Capacity;
            return QueryFullProcessImageName(process, 0, name, ref size) ? name.ToString() : null;
        }
        finally { CloseHandle(process); }
    }

    internal static string Class(nint h)
    {
        var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString();
    }
    internal static string Title(nint h)
    {
        var b = new StringBuilder(32768); GetWindowText(h, b, b.Capacity); return b.ToString();
    }
    internal static IReadOnlyList<nint> Children(nint h)
    {
        var result = new List<nint>();
        EnumChildWindows(h, (child, _) => { result.Add(child); return result.Count < 1024; }, 0);
        return result;
    }
    internal static long ProcessCreation(uint pid)
    {
        var p = OpenProcess(0x1000, false, pid);
        if (p == 0) return 0;
        try { return GetProcessTimes(p, out var created, out _, out _, out _) ? created : 0; }
        finally { CloseHandle(p); }
    }
    internal static bool TryReadText(nint h, out string text)
    {
        var b = new StringBuilder(32768);
        var ok = ReadTextMessage(h, 13, b.Capacity, b, 2, 250, out _);
        text = b.ToString(); return ok != 0;
    }
    internal static bool SetText(nint h, string text) => SendTextMessage(h, 12, 0, text, 2, 250, out var result) != 0 && result != 0;
    internal static nint Focus(uint thread)
    {
        var info = new GUITHREADINFO { Size = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(thread, ref info) ? info.Focus : 0;
    }
    internal static bool ModifiersReleased() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.All(k => (GetAsyncKeyState(k) & 0x8000) == 0);
    internal static uint? LastInputTick()
    {
        var info = new LASTINPUTINFO { Size = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? info.Time : null;
    }
    internal static bool TryReadSelection(nint window, out int start, out int end)
    {
        start = end = 0;
        if (!IsWindow(window) ||
            ReadSelectionMessage(window, 0xB0, out uint first, out uint last, 0x22, 150, out _) == 0 ||
            first > int.MaxValue || last > int.MaxValue) return false;
        start = (int)first; end = (int)last; return true;
    }
    internal static bool Keys(params (ushort Key, bool Up)[] keys) => KeysWithStamp(out _, keys);
    private static readonly nuint InputMask = IntPtr.Size == 8 ? unchecked((nuint)0xFFFFFFFF00000000UL) : 0xFFFF0000u;
    private static readonly nuint InputPrefix = IntPtr.Size == 8
        ? unchecked((nuint)((ulong)Random.Shared.NextInt64(1, int.MaxValue) << 32))
        : (nuint)((uint)Random.Shared.Next(1, 65536) << 16);
    private static int inputBatch;
    internal static bool IsOwnInput(nuint extra) => (extra & InputMask) == InputPrefix;
    internal static uint InputBatch(nuint extra) => unchecked((uint)(extra & ~InputMask));
    internal static bool KeysWithStamp(out uint stamp, params (ushort Key, bool Up)[] keys)
    {
        stamp = InputBatch(unchecked((nuint)Interlocked.Increment(ref inputBatch)));
        nuint extra = InputPrefix | stamp;
        var input = keys.Select(k => new INPUT { Type = 1, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { Key = k.Key, Flags = k.Up ? 2u : 0, Extra = extra } } }).ToArray();
        var count = SendInput((uint)input.Length, input, Marshal.SizeOf<INPUT>());
        if (count == input.Length) return true;
        // Only release keys this batch pressed; never continue a partial navigation.
        var release = keys.Where(k => !k.Up).Select(k => new INPUT { Type = 1, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { Key = k.Key, Flags = 2, Extra = extra } } }).ToArray();
        if (release.Length > 0) SendInput((uint)release.Length, release, Marshal.SizeOf<INPUT>());
        return false;
    }
    internal static Rectangle Frame(nint h)
    {
        if (DwmGetWindowAttribute(h, 9, out var rect, Marshal.SizeOf<RECT>()) == 0) return rect.ToRectangle();
        return GetWindowRect(h, out rect) ? rect.ToRectangle() : Rectangle.Empty;
    }
    internal static Rectangle WorkArea(nint h)
    {
        var info = new MONITORINFO { Size = (uint)Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(MonitorFromWindow(h, 2), ref info) ? info.Work.ToRectangle() : Screen.PrimaryScreen!.WorkingArea;
    }
}
