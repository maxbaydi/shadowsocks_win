using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using VibeShadowsocks.App.Services;
using VibeShadowsocks.App.ViewModels;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Extensions;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Core.Orchestration;
using VibeShadowsocks.Infrastructure.Extensions;
using VibeShadowsocks.Platform.Extensions;
using VibeShadowsocks.Platform.Hotkeys;
using VibeShadowsocks.Platform.SingleInstance;
using VibeShadowsocks.Platform.Tray;

namespace VibeShadowsocks.App;

public partial class App : Application
{
    private bool _isShuttingDown;
    private MainWindow? _window;

    private ITrayManager? _trayManager;
    private IHotkeyManager? _hotkeyManager;
    private ISingleInstanceManager? _singleInstanceManager;
    private IConnectionOrchestrator? _orchestrator;
    private ISettingsStore? _settingsStore;
    private DispatcherQueue? _dispatcherQueue;
    private ILogger<App>? _logger;

    public App()
    {
        InitializeComponent();
        Host = BuildHost();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public static IHost Host { get; private set; } = default!;

    public static T GetService<T>() where T : notnull => Host.Services.GetRequiredService<T>();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await Host.StartAsync();
            _logger = GetService<ILogger<App>>();

            _singleInstanceManager = GetService<ISingleInstanceManager>();
            var isPrimaryInstance = await _singleInstanceManager
                .InitializeAsync("default", CancellationToken.None);

            _logger.LogInformation("Single-instance check: IsPrimary={IsPrimary}", isPrimaryInstance);

            if (!isPrimaryInstance)
            {
                _logger.LogInformation("Secondary instance detected. Exiting after activation signal.");
                await Host.StopAsync();
                Exit();
                return;
            }

            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _trayManager = GetService<ITrayManager>();
            _hotkeyManager = GetService<IHotkeyManager>();
            _orchestrator = GetService<IConnectionOrchestrator>();
            _settingsStore = GetService<ISettingsStore>();

            _singleInstanceManager.ActivationRequested += OnActivationRequested;
            _trayManager.ConnectRequested += (_, _) => _ = _orchestrator.ConnectAsync();
            _trayManager.DisconnectRequested += (_, _) => _ = _orchestrator.DisconnectAsync();
            _trayManager.OpenRequested += (_, _) => ActivateMainWindow();
            _trayManager.ExitRequested += (_, _) => _ = ShutdownAsync();
            _trayManager.RoutingModeChanged += (_, mode) => _ = ApplyRoutingModeAsync(mode);
            _orchestrator.StateChanged += OnOrchestratorStateChanged;
            _hotkeyManager.HotkeyPressed += (_, _) => _ = _orchestrator.ToggleAsync();

            await _orchestrator.RecoverProxyStateAsync();

            var settings = await _settingsStore.LoadAsync();

            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            _trayManager.Initialize("VibeShadowsocks", iconPath);
            _trayManager.UpdateState(_orchestrator.Snapshot.State, settings.RoutingMode);

            if (!_hotkeyManager.Register(settings.Hotkey, out var hotkeyError))
            {
                _trayManager.ShowNotification("VibeShadowsocks", $"Hotkey conflict: {hotkeyError}");
            }

            _logger.LogInformation("Creating MainWindow...");
            _window = GetService<MainWindow>();
            _logger.LogInformation("MainWindow created. Activating...");
            _window.Activate();

            if (settings.AutoConnect)
            {
                _ = _orchestrator.ConnectAsync();
            }
        }
        catch (Exception exception)
        {
            try
            {
                _logger?.LogError(exception, "Fatal startup failure in OnLaunched.");
                Console.Error.WriteLine(exception);
            }
            catch
            {
                // ignored
            }

            await ShutdownAsync();
        }
    }

    private static IHost BuildHost()
    {
        return Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                services.AddVibeShadowsocksCore();
                services.AddVibeShadowsocksInfrastructure();
                services.AddVibeShadowsocksPlatform();

                services.AddSingleton<IClipboardService, ClipboardService>();

                services.AddSingleton<MainViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ServersViewModel>();
                services.AddTransient<RoutingViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<LogsViewModel>();

                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    private async void OnOrchestratorStateChanged(object? sender, ConnectionStateChangedEventArgs eventArgs)
    {
        if (_trayManager is null || _settingsStore is null)
        {
            return;
        }

        var settings = await _settingsStore.LoadAsync();

        _dispatcherQueue?.TryEnqueue(() =>
        {
            _trayManager.UpdateState(eventArgs.Snapshot.State, settings.RoutingMode);

            var (title, message) = eventArgs.Snapshot.State switch
            {
                ConnectionState.Connected => ("Connected", $"Server: {settings.GetActiveServerProfile()?.Name ?? "Unknown"}"),
                ConnectionState.Disconnected => ("Disconnected", "VPN tunnel closed"),
                ConnectionState.Faulted => ("Error", eventArgs.Snapshot.Message),
                _ => (null as string, null as string),
            };

            if (title is not null)
            {
                _trayManager.ShowNotification("VibeShadowsocks", $"{title} — {message}");
            }
        });
    }

    private async Task ApplyRoutingModeAsync(RoutingMode routingMode)
    {
        if (_settingsStore is null || _orchestrator is null)
        {
            return;
        }

        await _settingsStore
            .UpdateAsync(settings => settings with { RoutingMode = routingMode });

        await _orchestrator.ApplyRoutingAsync();
    }

    private void OnActivationRequested(object? sender, EventArgs eventArgs)
    {
        _dispatcherQueue?.TryEnqueue(ActivateMainWindow);
    }

    private void ActivateMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.ShowAndActivate();
    }

    private async void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs eventArgs)
    {
        try
        {
            _logger?.LogError(eventArgs.Exception, "Unhandled UI exception.");
            Console.Error.WriteLine(eventArgs.Exception);
        }
        catch
        {
            // ignored
        }

        eventArgs.Handled = true;

        if (_orchestrator is not null)
        {
            await _orchestrator.EmergencyRollbackAsync();
        }

        await ShutdownAsync();
    }

    private void OnProcessExit(object? sender, EventArgs eventArgs)
    {
        if (_orchestrator is null)
        {
            return;
        }

        try
        {
            _orchestrator.EmergencyRollbackAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore on shutdown.
        }
    }

    public async Task ShutdownAsync()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        try
        {
            if (_orchestrator is not null)
            {
                await _orchestrator.DisconnectAsync();
            }
        }
        catch
        {
            // continue shutdown.
        }

        _hotkeyManager?.Dispose();
        _trayManager?.Dispose();
        _singleInstanceManager?.Dispose();

        _window?.AllowClose();
        _window?.Close();

        await Host.StopAsync();
        Exit();
    }
}
