using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Platform.Hotkeys;

public interface IHotkeyManager : IDisposable
{
    event EventHandler? HotkeyPressed;

    HotkeyGesture? CurrentHotkey { get; }

    bool Register(HotkeyGesture gesture, out string error);

    void Unregister();
}
