using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Platform.Hotkeys;

public sealed class HotkeyManager(ILogger<HotkeyManager> logger) : IHotkeyManager
{
    private const int HotkeyId = 0x56494245;
    private const int WmHotKey = 0x0312;

    private readonly ILogger<HotkeyManager> _logger = logger;
    private readonly HotkeyWindow _window = new();

    public event EventHandler? HotkeyPressed;

    public HotkeyGesture? CurrentHotkey { get; private set; }

    public bool Register(HotkeyGesture gesture, out string error)
    {
        error = string.Empty;

        if (!TryParseVirtualKey(gesture.Key, out var virtualKey))
        {
            error = $"Unsupported key: {gesture.Key}";
            return false;
        }

        var modifiers = (uint)0;
        if (gesture.Alt)
        {
            modifiers |= 0x0001;
        }

        if (gesture.Ctrl)
        {
            modifiers |= 0x0002;
        }

        if (gesture.Shift)
        {
            modifiers |= 0x0004;
        }

        if (gesture.Win)
        {
            modifiers |= 0x0008;
        }

        Unregister();

        var success = RegisterHotKey(_window.Handle, HotkeyId, modifiers, virtualKey);
        if (!success)
        {
            var win32Error = Marshal.GetLastWin32Error();
            error = new Win32Exception(win32Error).Message;
            _logger.LogWarning("Failed to register global hotkey: {Error}", error);
            return false;
        }

        CurrentHotkey = gesture;
        _window.HotkeyPressed = () => HotkeyPressed?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Registered global hotkey {Hotkey}", gesture.Display);
        return true;
    }

    public void Unregister()
    {
        if (_window.Handle != nint.Zero)
        {
            _ = UnregisterHotKey(_window.Handle, HotkeyId);
        }

        CurrentHotkey = null;
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    private static bool TryParseVirtualKey(string key, out uint virtualKey)
    {
        if (Enum.TryParse<Keys>(key, ignoreCase: true, out var parsed))
        {
            virtualKey = (uint)parsed;
            return true;
        }

        if (key.Length == 1)
        {
            var upper = char.ToUpperInvariant(key[0]);
            if ((upper >= 'A' && upper <= 'Z') || (upper >= '0' && upper <= '9'))
            {
                virtualKey = upper;
                return true;
            }
        }

        virtualKey = 0;
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public Action? HotkeyPressed { get; set; }

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams
            {
                Caption = "VibeShadowsocksHotkeyWindow",
            });
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotKey)
            {
                HotkeyPressed?.Invoke();
            }

            base.WndProc(ref message);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }
}

