using System.Runtime.InteropServices;

namespace VibeShadowsocks.Infrastructure.Interop;

internal static class WinInetNative
{
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOptionW(nint hInternet, int dwOption, nint lpBuffer, int dwBufferLength);

    public static void RefreshInternetSettings()
    {
        if (!InternetSetOptionW(nint.Zero, InternetOptionSettingsChanged, nint.Zero, 0))
        {
            throw new InvalidOperationException($"InternetSetOption SETTINGS_CHANGED failed. Win32Error={Marshal.GetLastWin32Error()}");
        }

        if (!InternetSetOptionW(nint.Zero, InternetOptionRefresh, nint.Zero, 0))
        {
            throw new InvalidOperationException($"InternetSetOption REFRESH failed. Win32Error={Marshal.GetLastWin32Error()}");
        }
    }
}
