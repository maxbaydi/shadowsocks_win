using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using VibeShadowsocks.App.Services;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Core.Orchestration;

namespace VibeShadowsocks.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IConnectionOrchestrator _orchestrator;
    private readonly ISettingsStore _settingsStore;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IClipboardService _clipboardService;
    private readonly DispatcherQueue? _dispatcherQueue;

    [ObservableProperty]
    private string _stateText = "Disconnected";

    [ObservableProperty]
    private string _activeServerText = "Server: n/a";

    [ObservableProperty]
    private string _activePacText = "PAC: n/a";

    [ObservableProperty]
    private string _logPreview = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public DashboardViewModel(
        IConnectionOrchestrator orchestrator,
        ISettingsStore settingsStore,
        IDiagnosticsService diagnosticsService,
        IClipboardService clipboardService)
    {
        _orchestrator = orchestrator;
        _settingsStore = settingsStore;
        _diagnosticsService = diagnosticsService;
        _clipboardService = clipboardService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _orchestrator.StateChanged += OnStateChanged;
    }

    public async Task LoadAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        try
        {
            await _orchestrator.ConnectAsync();
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        IsBusy = true;
        try
        {
            await _orchestrator.DisconnectAsync();
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        IsBusy = true;
        try
        {
            await _orchestrator.ToggleAsync();
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task UpdatePacAsync()
    {
        IsBusy = true;
        try
        {
            await _orchestrator.UpdatePacAsync();
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task CopyDiagnosticsAsync()
    {
        var diagnostics = await _diagnosticsService.BuildTextReportAsync();
        _clipboardService.SetText(diagnostics);
        LogPreview = diagnostics;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var snapshot = _orchestrator.Snapshot;
        var settings = await _settingsStore.LoadAsync();
        var diagnosticsSnapshot = await _diagnosticsService.CaptureAsync();

        StateText = $"{snapshot.State} | {snapshot.Message}";
        ActiveServerText = $"Server: {settings.ActiveServerProfileId ?? "n/a"}";
        ActivePacText = $"PAC: {settings.ActivePacProfileId ?? "n/a"} ({settings.RoutingMode})";
        LogPreview = string.Join(Environment.NewLine, diagnosticsSnapshot.RecentLogLines.TakeLast(80));
        IsBusy = snapshot.State is ConnectionState.Starting or ConnectionState.Stopping;
    }

    private void OnStateChanged(object? sender, ConnectionStateChangedEventArgs args)
    {
        void Update()
        {
            StateText = $"{args.Snapshot.State} | {args.Snapshot.Message}";
            IsBusy = args.Snapshot.State is ConnectionState.Starting or ConnectionState.Stopping;
        }

        if (_dispatcherQueue is not null)
        {
            _dispatcherQueue.TryEnqueue(Update);
        }
        else
        {
            Update();
        }
    }
}

