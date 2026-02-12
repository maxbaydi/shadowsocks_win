using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Orchestration;

public sealed class ConnectionOrchestrator : IConnectionOrchestrator, IDisposable
{
    private sealed record QueueItem(
        OrchestratorCommand Command,
        CancellationToken CancellationToken,
        TaskCompletionSource<OrchestrationResult> CompletionSource);

    private readonly ILogger<ConnectionOrchestrator> _logger;
    private readonly ISettingsStore _settingsStore;
    private readonly ISettingsValidator _settingsValidator;
    private readonly ISecureStorage _secureStorage;
    private readonly ISsLocalRunner _ssLocalRunner;
    private readonly ISystemProxyManager _proxyManager;
    private readonly IPacManager _pacManager;
    private readonly IPortAvailabilityProbe _portProbe;
    private readonly ISsLocalProvisioner _ssLocalProvisioner;
    private readonly ConnectionOrchestratorOptions _options;

    private readonly Channel<QueueItem> _commands = Channel.CreateUnbounded<QueueItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly CancellationTokenSource _queueCts = new();
    private readonly ConcurrentDictionary<OrchestratorCommandType, byte> _coalescedQueued = new();

    private readonly Task _processingLoop;

    private volatile ConnectionStatusSnapshot _snapshot =
        new(ConnectionState.Disconnected, string.Empty, DateTimeOffset.UtcNow, null, null, null);

    private string? _connectedServerProfileId;
    private string? _connectedPacProfileId;
    private int _connectedSocksPort;
    private int _connectedHttpPort;
    private int _connectedPacPort;

    public ConnectionOrchestrator(
        ILogger<ConnectionOrchestrator> logger,
        ISettingsStore settingsStore,
        ISettingsValidator settingsValidator,
        ISecureStorage secureStorage,
        ISsLocalRunner ssLocalRunner,
        ISystemProxyManager proxyManager,
        IPacManager pacManager,
        IPortAvailabilityProbe portProbe,
        ISsLocalProvisioner ssLocalProvisioner,
        ConnectionOrchestratorOptions? options = null)
    {
        _logger = logger;
        _settingsStore = settingsStore;
        _settingsValidator = settingsValidator;
        _secureStorage = secureStorage;
        _ssLocalRunner = ssLocalRunner;
        _proxyManager = proxyManager;
        _pacManager = pacManager;
        _portProbe = portProbe;
        _ssLocalProvisioner = ssLocalProvisioner;
        _options = options ?? new ConnectionOrchestratorOptions();

        _ssLocalRunner.UnexpectedExit += OnUnexpectedExit;
        _processingLoop = Task.Run(() => ProcessLoopAsync(_queueCts.Token));
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public ConnectionStatusSnapshot Snapshot => _snapshot;

    public Task<OrchestrationResult> ConnectAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(OrchestratorCommand.Connect(), cancellationToken);

    public Task<OrchestrationResult> DisconnectAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(OrchestratorCommand.Disconnect(), cancellationToken);

    public Task<OrchestrationResult> ToggleAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(OrchestratorCommand.Toggle(), cancellationToken);

    public Task<OrchestrationResult> ApplyRoutingAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(OrchestratorCommand.ApplyRouting(), cancellationToken);

    public Task<OrchestrationResult> UpdatePacAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(OrchestratorCommand.UpdatePac(), cancellationToken);

    public Task<OrchestrationResult> UpdateListsAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(OrchestratorCommand.UpdateLists(), cancellationToken);

    public Task<OrchestrationResult> RecoverProxyStateAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(OrchestratorCommand.RecoverProxy(), cancellationToken);

    public async Task EmergencyRollbackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _proxyManager.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Emergency rollback failed.");
        }

        try
        {
            await _pacManager.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "PAC shutdown failed during emergency rollback.");
        }
    }

    public async Task<OrchestrationResult> EnqueueAsync(OrchestratorCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (IsCoalesced(command.Type) && !_coalescedQueued.TryAdd(command.Type, 0))
        {
            return OrchestrationResult.NoOp($"{command.Type} is already queued.");
        }

        var completionSource = new TaskCompletionSource<OrchestrationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new QueueItem(command, cancellationToken, completionSource);

        if (!_commands.Writer.TryWrite(item))
        {
            RemoveCoalesced(command.Type);
            return OrchestrationResult.Failed("Cannot enqueue command.");
        }

        return await completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _queueCts.Cancel();
        _commands.Writer.TryComplete();

        try
        {
            _processingLoop.Wait(_options.QueueShutdownTimeout);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Command queue loop shutdown completed with a handled exception.");
        }

        _ssLocalRunner.UnexpectedExit -= OnUnexpectedExit;
        _queueCts.Dispose();
    }

    private static bool IsCoalesced(OrchestratorCommandType type) =>
        type is OrchestratorCommandType.ApplyRouting or OrchestratorCommandType.UpdatePac or OrchestratorCommandType.UpdateLists;

    private void RemoveCoalesced(OrchestratorCommandType type)
    {
        if (IsCoalesced(type))
        {
            _coalescedQueued.TryRemove(type, out _);
        }
    }

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        while (await _commands.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_commands.Reader.TryRead(out var item))
            {
                try
                {
                    if (item.CancellationToken.IsCancellationRequested)
                    {
                        item.CompletionSource.TrySetCanceled(item.CancellationToken);
                        continue;
                    }

                    var result = await ExecuteCommandAsync(item.Command, item.CancellationToken).ConfigureAwait(false);
                    item.CompletionSource.TrySetResult(result);
                }
                catch (OperationCanceledException cancellationException)
                {
                    _logger.LogDebug(cancellationException, "Orchestrator command canceled: {CommandType}", item.Command.Type);
                    item.CompletionSource.TrySetCanceled(cancellationException.CancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unhandled orchestration error for command {CommandType}", item.Command.Type);
                    item.CompletionSource.TrySetResult(OrchestrationResult.Failed(exception.Message));
                }
                finally
                {
                    RemoveCoalesced(item.Command.Type);
                }
            }
        }
    }

    private async Task<OrchestrationResult> ExecuteCommandAsync(OrchestratorCommand command, CancellationToken cancellationToken)
    {
        return command.Type switch
        {
            OrchestratorCommandType.Connect => await HandleConnectAsync(cancellationToken).ConfigureAwait(false),
            OrchestratorCommandType.Disconnect => await HandleDisconnectAsync(cancellationToken).ConfigureAwait(false),
            OrchestratorCommandType.Toggle => await HandleToggleAsync(cancellationToken).ConfigureAwait(false),
            OrchestratorCommandType.ApplyRouting => await HandleApplyRoutingAsync(cancellationToken).ConfigureAwait(false),
            OrchestratorCommandType.UpdatePac => await HandleUpdatePacAsync(cancellationToken).ConfigureAwait(false),
            OrchestratorCommandType.UpdateLists => await HandleUpdateListsAsync(cancellationToken).ConfigureAwait(false),
            OrchestratorCommandType.RecoverProxy => await HandleRecoverProxyAsync(cancellationToken).ConfigureAwait(false),
            OrchestratorCommandType.FaultFromProcessExit => await HandleRunnerFaultAsync("sslocal exited unexpectedly", cancellationToken).ConfigureAwait(false),
            _ => OrchestrationResult.Failed($"Unknown command: {command.Type}"),
        };
    }

    private async Task<OrchestrationResult> HandleRecoverProxyAsync(CancellationToken cancellationToken)
    {
        await _proxyManager.CrashRecoverIfNeededAsync(cancellationToken).ConfigureAwait(false);
        return OrchestrationResult.Ok("Proxy recovery completed.");
    }

    private async Task<OrchestrationResult> HandleConnectAsync(CancellationToken cancellationToken)
    {
        if (_snapshot.State is ConnectionState.Connected or ConnectionState.Starting)
        {
            return OrchestrationResult.NoOp("Already connected or connecting.");
        }

        if (_snapshot.State == ConnectionState.Stopping)
        {
            return OrchestrationResult.NoOp("Cannot connect while disconnecting.");
        }

        SetState(ConnectionState.Starting, "Starting sslocal...");

        AppSettings settings;
        try
        {
            settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var validationErrors = _settingsValidator.Validate(settings);
            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, validationErrors));
            }

            var activeProfile = settings.GetActiveServerProfile() ??
                throw new InvalidOperationException("Active server profile is not configured.");
            var password = await _secureStorage.ReadSecretAsync(activeProfile.PasswordSecretId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Server password is missing in secure storage.");
            }

            var socksPort = await ResolvePortAsync(settings.Ports.SocksPort, settings.Ports.AutoSelectOnConflict, cancellationToken).ConfigureAwait(false);
            var httpPort = settings.Ports.HttpPort > 0
                ? await ResolvePortAsync(settings.Ports.HttpPort, settings.Ports.AutoSelectOnConflict, cancellationToken).ConfigureAwait(false)
                : 0;
            var pacPort = await ResolvePortAsync(settings.Ports.PacServerPort, settings.Ports.AutoSelectOnConflict, cancellationToken).ConfigureAwait(false);
            var listenAddress = settings.Ports.ListenAddress;

            var runtimePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VibeShadowsocks",
                "runtime");
            Directory.CreateDirectory(runtimePath);

            using var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startTimeout.CancelAfter(_options.StartTimeout);

            var executablePath = await _ssLocalProvisioner
                .EnsureAvailableAsync(settings.SsLocalExecutablePath, cancellationToken)
                .ConfigureAwait(false);

            var startResult = await _ssLocalRunner.StartAsync(
                new SsLocalStartRequest(
                    executablePath,
                    activeProfile,
                    password,
                    socksPort,
                    httpPort,
                    listenAddress,
                    runtimePath,
                    UseServerUrlMode: false),
                startTimeout.Token).ConfigureAwait(false);

            var healthy = await _ssLocalRunner
                .WaitForLocalPortAsync(socksPort, _options.StartTimeout, startTimeout.Token)
                .ConfigureAwait(false);

            if (!healthy)
            {
                throw new TimeoutException($"sslocal did not open localhost:{socksPort} in time.");
            }

            Uri? pacUri = null;
            if (settings.RoutingMode != RoutingMode.Off)
            {
                await _proxyManager.BeginSessionAsync(startTimeout.Token).ConfigureAwait(false);
                if (settings.RoutingMode == RoutingMode.Pac)
                {
                    var pacProfile = settings.GetActivePacProfile() ??
                        throw new InvalidOperationException("Active PAC profile is not configured.");
                    pacUri = await _pacManager.ResolvePacUriAsync(pacProfile, socksPort, pacPort, startTimeout.Token).ConfigureAwait(false);
                    _connectedPacProfileId = pacProfile.Id;
                }

                await _proxyManager.ApplyRoutingModeAsync(settings.RoutingMode, socksPort, httpPort, pacUri, startTimeout.Token).ConfigureAwait(false);
            }

            _connectedServerProfileId = activeProfile.Id;
            _connectedSocksPort = socksPort;
            _connectedHttpPort = httpPort;
            _connectedPacPort = pacPort;

            SetState(ConnectionState.Connected, "Connected", activeProfile.Id, _connectedPacProfileId, startResult.ProcessId);
            return OrchestrationResult.Ok("Connected.");
        }
        catch (OperationCanceledException)
        {
            await FailStartAsync("Connection was canceled.", cancellationToken).ConfigureAwait(false);
            return OrchestrationResult.Failed("Connect canceled.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Connection failed.");
            await FailStartAsync($"Connection failed: {exception.Message}", cancellationToken).ConfigureAwait(false);
            return OrchestrationResult.Failed(exception.Message);
        }
    }

    private async Task<OrchestrationResult> HandleDisconnectAsync(CancellationToken cancellationToken)
    {
        if (_snapshot.State is ConnectionState.Disconnected)
        {
            return OrchestrationResult.NoOp("Already disconnected.");
        }

        SetState(ConnectionState.Stopping, "Disconnecting...");

        var errors = new List<Exception>();
        using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopTimeout.CancelAfter(_options.StopTimeout);

        try
        {
            await _proxyManager.RollbackAsync(stopTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(exception);
            _logger.LogError(exception, "Proxy rollback failed during disconnect.");
        }

        try
        {
            await _pacManager.StopAsync(stopTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(exception);
            _logger.LogError(exception, "PAC manager stop failed during disconnect.");
        }

        try
        {
            await _ssLocalRunner.StopAsync(_options.StopTimeout, stopTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(exception);
            _logger.LogError(exception, "sslocal stop failed during disconnect.");
        }

        _connectedServerProfileId = null;
        _connectedPacProfileId = null;
        _connectedSocksPort = 0;
        _connectedHttpPort = 0;
        _connectedPacPort = 0;

        if (errors.Count > 0)
        {
            SetState(ConnectionState.Faulted, "Disconnected with errors.");
            return OrchestrationResult.Failed($"Disconnect completed with {errors.Count} errors.");
        }

        SetState(ConnectionState.Disconnected, "Disconnected");
        return OrchestrationResult.Ok("Disconnected.");
    }

    private async Task<OrchestrationResult> HandleToggleAsync(CancellationToken cancellationToken)
    {
        return _snapshot.State is ConnectionState.Connected or ConnectionState.Starting
            ? await HandleDisconnectAsync(cancellationToken).ConfigureAwait(false)
            : await HandleConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<OrchestrationResult> HandleApplyRoutingAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (_snapshot.State != ConnectionState.Connected)
        {
            return OrchestrationResult.NoOp("Routing update saved, current state is not Connected.");
        }

        using var proxyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        proxyTimeout.CancelAfter(_options.ProxyApplyTimeout);

        Uri? pacUri = null;
        if (settings.RoutingMode == RoutingMode.Pac)
        {
            var pacProfile = settings.GetActivePacProfile() ??
                throw new InvalidOperationException("Active PAC profile is not configured.");
            pacUri = await _pacManager.ResolvePacUriAsync(pacProfile, _connectedSocksPort, _connectedPacPort, proxyTimeout.Token).ConfigureAwait(false);
            _connectedPacProfileId = pacProfile.Id;
        }

        if (settings.RoutingMode != RoutingMode.Off)
        {
            await _proxyManager.BeginSessionAsync(proxyTimeout.Token).ConfigureAwait(false);
        }

        await _proxyManager.ApplyRoutingModeAsync(settings.RoutingMode, _connectedSocksPort, _connectedHttpPort, pacUri, proxyTimeout.Token).ConfigureAwait(false);
        SetState(ConnectionState.Connected, $"Routing mode applied: {settings.RoutingMode}", _connectedServerProfileId, _connectedPacProfileId, _ssLocalRunner.ProcessId);
        return OrchestrationResult.Ok("Routing mode applied.");
    }

    private async Task<OrchestrationResult> HandleUpdatePacAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var pacProfile = settings.GetActivePacProfile();
        if (pacProfile is null)
        {
            return OrchestrationResult.NoOp("No active PAC profile.");
        }

        var updateResult = await _pacManager.UpdateManagedPacAsync(pacProfile, _connectedSocksPort > 0 ? _connectedSocksPort : settings.Ports.SocksPort, cancellationToken).ConfigureAwait(false);
        if (_snapshot.State == ConnectionState.Connected && settings.RoutingMode == RoutingMode.Pac)
        {
            await HandleApplyRoutingAsync(cancellationToken).ConfigureAwait(false);
        }

        return updateResult.Applied
            ? OrchestrationResult.Ok(updateResult.Message)
            : OrchestrationResult.Failed(updateResult.Message);
    }

    private async Task<OrchestrationResult> HandleUpdateListsAsync(CancellationToken cancellationToken)
    {
        return await HandleUpdatePacAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<OrchestrationResult> HandleRunnerFaultAsync(string reason, CancellationToken cancellationToken)
    {
        if (_snapshot.State is ConnectionState.Disconnected or ConnectionState.Stopping)
        {
            return OrchestrationResult.NoOp("Runner fault ignored because tunnel is stopping/disconnected.");
        }

        SetState(ConnectionState.Faulted, reason, _connectedServerProfileId, _connectedPacProfileId, _ssLocalRunner.ProcessId);

        try
        {
            await _proxyManager.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Proxy rollback failed after sslocal unexpected exit.");
        }

        try
        {
            await _pacManager.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "PAC manager stop failed after sslocal unexpected exit.");
        }

        return OrchestrationResult.Failed(reason);
    }

    private async Task FailStartAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await _proxyManager.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Proxy rollback after failed start failed.");
        }

        try
        {
            await _pacManager.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "PAC manager stop after failed start failed.");
        }

        try
        {
            await _ssLocalRunner.StopAsync(_options.StopTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "sslocal stop after failed start failed.");
        }

        SetState(ConnectionState.Faulted, message, _connectedServerProfileId, _connectedPacProfileId, _ssLocalRunner.ProcessId);
    }

    private async Task<int> ResolvePortAsync(int preferredPort, bool autoSelect, CancellationToken cancellationToken)
    {
        if (await _portProbe.IsPortAvailableAsync(preferredPort, cancellationToken).ConfigureAwait(false))
        {
            return preferredPort;
        }

        if (!autoSelect)
        {
            throw new InvalidOperationException($"Port {preferredPort} is already in use.");
        }

        var alternative = await _portProbe.FindAvailablePortAsync(preferredPort, cancellationToken).ConfigureAwait(false);
        _logger.LogWarning("Port {PreferredPort} is busy, using {AlternativePort}.", preferredPort, alternative);
        return alternative;
    }


    private void OnUnexpectedExit(object? sender, SsLocalExitedEventArgs eventArgs)
    {
        _logger.LogError("sslocal exited unexpectedly with code {ExitCode}: {Reason}", eventArgs.ExitCode, eventArgs.Reason);
        _ = EnqueueAsync(new OrchestratorCommand(OrchestratorCommandType.FaultFromProcessExit, eventArgs));
    }

    private void SetState(
        ConnectionState state,
        string message,
        string? activeProfileId = null,
        string? activePacProfileId = null,
        int? processId = null)
    {
        var newSnapshot = new ConnectionStatusSnapshot(
            state,
            message,
            DateTimeOffset.UtcNow,
            activeProfileId,
            activePacProfileId,
            processId);

        _snapshot = newSnapshot;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(newSnapshot));
    }

    private void ThrowIfDisposed()
    {
        if (_queueCts.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(ConnectionOrchestrator));
        }
    }
}
