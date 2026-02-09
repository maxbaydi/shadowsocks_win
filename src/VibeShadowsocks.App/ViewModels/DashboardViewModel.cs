using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using VibeShadowsocks.App.Helpers;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Core.Orchestration;

namespace VibeShadowsocks.App.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly IConnectionOrchestrator _orchestrator;
    private readonly ISettingsStore _settingsStore;
    private readonly DispatcherQueue? _dispatcherQueue;

    private bool _suppressSelectionHandlers;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private string _stateText = "Disconnected";

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ServerProfile> _serverProfiles = [];

    [ObservableProperty]
    private ServerProfile? _selectedServer;

    [ObservableProperty]
    private string _selectedRoutingMode = "Off";

    [ObservableProperty]
    private string _activePacName = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canConnect;

    [ObservableProperty]
    private bool _canDisconnect;

    [ObservableProperty]
    private bool _isErrorVisible;

    public string TooltipRefreshStatus => Loc.Get("TooltipRefreshStatus");

    public IReadOnlyList<string> RoutingModes { get; } = ["Off", "Global", "Pac"];

    public DashboardViewModel(
        IConnectionOrchestrator orchestrator,
        ISettingsStore settingsStore)
    {
        _orchestrator = orchestrator;
        _settingsStore = settingsStore;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _stateMessage = Loc.Get("ReadyToConnect");

        _orchestrator.StateChanged += OnStateChanged;
    }

    public async Task LoadAsync()
    {
        await RefreshAsync();
    }

    async partial void OnSelectedRoutingModeChanged(string value)
    {
        if (_suppressSelectionHandlers || !Enum.TryParse<RoutingMode>(value, ignoreCase: true, out var routingMode))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _settingsStore.UpdateAsync(s => s with { RoutingMode = routingMode });
            await _orchestrator.ApplyRoutingAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    async partial void OnSelectedServerChanged(ServerProfile? value)
    {
        if (_suppressSelectionHandlers || value is null)
        {
            return;
        }

        IsBusy = true;
        IsErrorVisible = false;
        try
        {
            await _settingsStore.UpdateAsync(s => s with { ActiveServerProfileId = value.Id });

            var wasConnected = _orchestrator.Snapshot.State == ConnectionState.Connected;
            if (wasConnected)
            {
                await _orchestrator.DisconnectAsync();
                var result = await _orchestrator.ConnectAsync();
                if (!result.Success)
                {
                    StateMessage = result.Message;
                    IsErrorVisible = true;
                }
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        IsErrorVisible = false;
        IsBusy = true;
        try
        {
            var result = await _orchestrator.ConnectAsync();
            if (!result.Success)
            {
                StateMessage = result.Message;
                IsErrorVisible = true;
            }
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
        IsErrorVisible = false;
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
        IsErrorVisible = false;
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
    private async Task RefreshAsync()
    {
        _suppressSelectionHandlers = true;
        try
        {
            var snapshot = _orchestrator.Snapshot;
            var settings = await _settingsStore.LoadAsync();

            ConnectionState = snapshot.State;
            StateText = LocalizeState(snapshot.State);
            StateMessage = LocalizeMessage(snapshot.State, snapshot.Message);

            ServerProfiles = new ObservableCollection<ServerProfile>(settings.ServerProfiles);
            SelectedServer = settings.GetActiveServerProfile();

            SelectedRoutingMode = settings.RoutingMode.ToString();
            var activePac = settings.GetActivePacProfile();
            ActivePacName = activePac?.Name ?? string.Empty;

            UpdateCommandStates(snapshot.State);
        }
        finally
        {
            _suppressSelectionHandlers = false;
        }
    }

    public void Dispose()
    {
        _orchestrator.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, ConnectionStateChangedEventArgs args)
    {
        void Update()
        {
            ConnectionState = args.Snapshot.State;
            StateText = LocalizeState(args.Snapshot.State);
            StateMessage = LocalizeMessage(args.Snapshot.State, args.Snapshot.Message);
            IsErrorVisible = args.Snapshot.State == ConnectionState.Faulted;
            UpdateCommandStates(args.Snapshot.State);
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

    private void UpdateCommandStates(ConnectionState state)
    {
        IsBusy = state is ConnectionState.Starting or ConnectionState.Stopping;
        CanConnect = state is ConnectionState.Disconnected or ConnectionState.Faulted;
        CanDisconnect = state is ConnectionState.Connected;
    }

    private static string LocalizeState(ConnectionState state) => state switch
    {
        ConnectionState.Disconnected => Loc.Get("StateDisconnected"),
        ConnectionState.Starting => Loc.Get("StateStarting"),
        ConnectionState.Connected => Loc.Get("StateConnected"),
        ConnectionState.Stopping => Loc.Get("StateStopping"),
        ConnectionState.Faulted => Loc.Get("StateFaulted"),
        _ => state.ToString(),
    };

    private static string LocalizeMessage(ConnectionState state, string rawMessage) => state switch
    {
        ConnectionState.Disconnected when string.IsNullOrEmpty(rawMessage) => Loc.Get("ReadyToConnect"),
        ConnectionState.Starting => Loc.Get("MsgStarting"),
        ConnectionState.Connected when string.IsNullOrEmpty(rawMessage) => Loc.Get("MsgConnected"),
        ConnectionState.Stopping => Loc.Get("MsgStopping"),
        _ => rawMessage,
    };
}
