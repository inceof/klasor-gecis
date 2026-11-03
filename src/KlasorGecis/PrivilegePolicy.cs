using System.Runtime.InteropServices;

namespace KlasorGecis;

/// <summary>
/// Never requests elevation. Used to prevent this app itself from running
/// elevated; target dialogs can be attempted on a best-effort basis under UIPI.
/// </summary>
internal static class PrivilegePolicy
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevationData
    {
        public uint TokenIsElevated;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint tokenHandle, int informationClass,
        out TokenElevationData information, int informationLength, out int returnedLength);

    internal static bool TryIsElevated(uint processId, out bool elevated)
    {
        elevated = false;
        if (processId == 0) return false;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0) return false;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token)) return false;
            try
            {
                var size = Marshal.SizeOf<TokenElevationData>();
                if (!GetTokenInformation(token, TokenElevation, out var data, size, out var resultSize)
                    || resultSize != size) return false;
                elevated = data.TokenIsElevated != 0;
                return true;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

}
