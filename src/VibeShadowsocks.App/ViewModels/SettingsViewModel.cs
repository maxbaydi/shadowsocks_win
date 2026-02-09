using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Platform.Hotkeys;
using VibeShadowsocks.Platform.Startup;

namespace VibeShadowsocks.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IHotkeyManager _hotkeyManager;
    private readonly IStartupManager _startupManager;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _autoConnect;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose;

    [ObservableProperty]
    private string _ssLocalPath = string.Empty;

    [ObservableProperty]
    private int _socksPort = 1080;

    [ObservableProperty]
    private int _pacPort = 8090;

    [ObservableProperty]
    private string _hotkeyText = "Ctrl+Alt+Shift+P";

    [ObservableProperty]
    private string _logLevel = "Information";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SettingsViewModel(ISettingsStore settingsStore, IHotkeyManager hotkeyManager, IStartupManager startupManager)
    {
        _settingsStore = settingsStore;
        _hotkeyManager = hotkeyManager;
        _startupManager = startupManager;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var settings = await _settingsStore.LoadAsync();

        AutoStart = await _startupManager.IsEnabledAsync();
        AutoConnect = settings.AutoConnect;
        MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
        SsLocalPath = settings.SsLocalExecutablePath ?? string.Empty;
        SocksPort = settings.Ports.SocksPort;
        PacPort = settings.Ports.PacServerPort;
        HotkeyText = settings.Hotkey.Display;
        LogLevel = settings.LogLevel;
        StatusMessage = "Settings loaded.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!TryParseHotkey(HotkeyText, out var hotkey, out var hotkeyError))
        {
            StatusMessage = hotkeyError;
            return;
        }

        if (!_hotkeyManager.Register(hotkey, out var registrationError))
        {
            StatusMessage = $"Hotkey conflict: {registrationError}";
            return;
        }

        await _startupManager.SetEnabledAsync(AutoStart);

        await _settingsStore.UpdateAsync(settings => settings with
        {
            AutoStart = AutoStart,
            AutoConnect = AutoConnect,
            MinimizeToTrayOnClose = MinimizeToTrayOnClose,
            SsLocalExecutablePath = string.IsNullOrWhiteSpace(SsLocalPath) ? null : SsLocalPath,
            Ports = settings.Ports with
            {
                SocksPort = SocksPort,
                PacServerPort = PacPort,
            },
            Hotkey = hotkey,
            LogLevel = LogLevel,
        });

        StatusMessage = "Settings saved.";
    }

    private static bool TryParseHotkey(string value, out HotkeyGesture gesture, out string error)
    {
        error = string.Empty;
        gesture = new HotkeyGesture();

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Hotkey string is empty.";
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Hotkey string is invalid.";
            return false;
        }

        var ctrl = parts.Any(part => part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase));
        var alt = parts.Any(part => part.Equals("Alt", StringComparison.OrdinalIgnoreCase));
        var shift = parts.Any(part => part.Equals("Shift", StringComparison.OrdinalIgnoreCase));
        var win = parts.Any(part => part.Equals("Win", StringComparison.OrdinalIgnoreCase));
        var key = parts.Last();

        if (key.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Win", StringComparison.OrdinalIgnoreCase))
        {
            error = "Hotkey key part is missing.";
            return false;
        }

        gesture = new HotkeyGesture
        {
            Ctrl = ctrl,
            Alt = alt,
            Shift = shift,
            Win = win,
            Key = key,
        };

        return true;
    }
}
