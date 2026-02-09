using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VibeShadowsocks.App.ViewModels;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using WinRT.Interop;

namespace VibeShadowsocks.App;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsStore _settingsStore;
    private readonly AppWindow _appWindow;
    private bool _allowClose;
    private bool _minimizeToTrayOnClose = true;

    public MainWindow(MainViewModel viewModel, ISettingsStore settingsStore)
    {
        _viewModel = viewModel;
        _settingsStore = settingsStore;

        InitializeComponent();

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += OnAppWindowClosing;
        _appWindow.Resize(new Windows.Graphics.SizeInt32(960, 680));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        RootNavigation.SelectedItem = RootNavigation.MenuItems.FirstOrDefault();
        ContentFrame.Navigate(typeof(Views.DashboardPage));

        _ = CacheSettingsAsync();
    }

    public void ShowAndActivate()
    {
        _appWindow.Show();
        Activate();

        var hwnd = WindowNative.GetWindowHandle(this);
        _ = SetForegroundWindow(hwnd);
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private async Task CacheSettingsAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync();
            _minimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
        }
        catch
        {
            _minimizeToTrayOnClose = true;
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item)
        {
            return;
        }

        var pageType = item.Tag?.ToString() switch
        {
            "dashboard" => typeof(Views.DashboardPage),
            "servers" => typeof(Views.ServersPage),
            "routing" => typeof(Views.RoutingPage),
            "settings" => typeof(Views.SettingsPage),
            "logs" => typeof(Views.LogsPage),
            _ => typeof(Views.DashboardPage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        if (_minimizeToTrayOnClose)
        {
            args.Cancel = true;
            sender.Hide();
        }
    }
}

