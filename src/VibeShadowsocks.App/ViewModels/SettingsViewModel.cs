using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VibeShadowsocks.App.Helpers;
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
    private readonly IUpdateService _updateService;
    private string _savedLanguage = "English";

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

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _selectedLanguage = "English";

    [ObservableProperty]
    private string _languageStatusMessage = string.Empty;

    [ObservableProperty]
    private string _currentVersion = string.Empty;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private int _downloadProgress;

    public IReadOnlyList<string> LogLevels { get; } = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    public IReadOnlyList<string> Languages { get; } = ["English", "Русский"];

    public SettingsViewModel(
        ISettingsStore settingsStore,
        IHotkeyManager hotkeyManager,
        IStartupManager startupManager,
        IUpdateService updateService)
    {
        _settingsStore = settingsStore;
        _hotkeyManager = hotkeyManager;
        _startupManager = startupManager;
        _updateService = updateService;
        CurrentVersion = Loc.Format("CurrentVersionFmt", _updateService.CurrentVersion ?? "—");
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (value != _savedLanguage)
        {
            LanguageStatusMessage = Loc.Get("RestartForLanguage");
        }
        else
        {
            LanguageStatusMessage = string.Empty;
        }
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
        SelectedLanguage = LanguageCodeToDisplay(settings.Language);
        _savedLanguage = SelectedLanguage;
        LanguageStatusMessage = string.Empty;
        StatusMessage = Loc.Get("SettingsLoaded");
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
            StatusMessage = Loc.Format("HotkeyConflictFmt", registrationError);
            return;
        }

        IsBusy = true;
        try
        {
            await _startupManager.SetEnabledAsync(AutoStart);

            var languageCode = DisplayToLanguageCode(SelectedLanguage);

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
                Language = languageCode,
            });

            _savedLanguage = SelectedLanguage;
            StatusMessage = Loc.Get("SettingsSaved");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = Loc.Get("CheckingForUpdates");
        IsUpdateAvailable = false;

        try
        {
            var info = await _updateService.CheckForUpdateAsync();
            if (info is not null)
            {
                IsUpdateAvailable = true;
                UpdateStatus = Loc.Format("UpdateAvailableFmt", info.TargetVersion);
            }
            else
            {
                UpdateStatus = Loc.Get("NoUpdates");
            }
        }
        catch
        {
            UpdateStatus = Loc.Get("UpdateCheckFailed");
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAndApplyUpdateAsync()
    {
        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        UpdateStatus = Loc.Get("Downloading");

        try
        {
            await _updateService.DownloadUpdateAsync(p =>
            {
                DownloadProgress = p;
            });

            UpdateStatus = Loc.Get("InstallingUpdate");
            _updateService.ApplyUpdateAndRestart();
        }
        catch
        {
            UpdateStatus = Loc.Get("UpdateDownloadFailed");
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private static bool TryParseHotkey(string value, out HotkeyGesture gesture, out string error)
    {
        error = string.Empty;
        gesture = new HotkeyGesture();

        if (string.IsNullOrWhiteSpace(value))
        {
            error = Loc.Get("HotkeyEmpty");
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = Loc.Get("HotkeyInvalid");
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
            error = Loc.Get("HotkeyMissingKey");
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

    private static string LanguageCodeToDisplay(string code) => code switch
    {
        "ru-RU" => "Русский",
        _ => "English",
    };

    private static string DisplayToLanguageCode(string display) => display switch
    {
        "Русский" => "ru-RU",
        _ => "en-US",
    };
}
