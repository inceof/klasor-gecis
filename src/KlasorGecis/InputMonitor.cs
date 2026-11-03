using System.Runtime.InteropServices;

namespace KlasorGecis;

// Scoped to an in-flight navigation. Callbacks count events only: no keys,
// pointer positions or text are retained, and input is always passed through.
internal sealed class InputMonitor : IDisposable
{
    private delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData
    { public uint Key, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    private readonly HookProc keyboardCallback, mouseCallback;
    private nint keyboard, mouse;
    private long epoch;
    private uint lastBatch;
    private bool received;
    internal bool Available => keyboard != 0 && mouse != 0;
    internal long Epoch => epoch;
    internal bool Unchanged(long expected) => Available && epoch == expected;
    internal bool Received(uint batch) => Available && received && lastBatch == batch;

    internal InputMonitor()
    {
        keyboardCallback = Keyboard; mouseCallback = Mouse;
        nint module = GetModuleHandle(null);
        keyboard = SetWindowsHookEx(13, keyboardCallback, module, 0);
        mouse = SetWindowsHookEx(14, mouseCallback, module, 0);
        if (!Available) Dispose();
    }

    private nint Keyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            nuint extra = Marshal.PtrToStructure<KeyboardData>(data).Extra;
            if (Native.IsOwnInput(extra)) { lastBatch = Native.InputBatch(extra); received = true; }
            else epoch++;
        }
        return CallNextHookEx(0, code, message, data);
    }

    private nint Mouse(int code, nint message, nint data)
    {
        if (code >= 0 && !Native.IsOwnInput(Marshal.PtrToStructure<MouseData>(data).Extra)) epoch++;
        return CallNextHookEx(0, code, message, data);
    }

    public void Dispose()
    {
        if (keyboard != 0) { UnhookWindowsHookEx(keyboard); keyboard = 0; }
        if (mouse != 0) { UnhookWindowsHookEx(mouse); mouse = 0; }
        GC.KeepAlive(keyboardCallback); GC.KeepAlive(mouseCallback);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int kind, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
