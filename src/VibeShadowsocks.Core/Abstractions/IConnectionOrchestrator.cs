using VibeShadowsocks.Core.Orchestration;

namespace VibeShadowsocks.Core.Abstractions;

public interface IConnectionOrchestrator
{
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    ConnectionStatusSnapshot Snapshot { get; }

    Task<OrchestrationResult> EnqueueAsync(OrchestratorCommand command, CancellationToken cancellationToken = default);

    Task<OrchestrationResult> ConnectAsync(CancellationToken cancellationToken = default);

    Task<OrchestrationResult> DisconnectAsync(CancellationToken cancellationToken = default);

    Task<OrchestrationResult> ToggleAsync(CancellationToken cancellationToken = default);

    Task<OrchestrationResult> ApplyRoutingAsync(CancellationToken cancellationToken = default);

    Task<OrchestrationResult> UpdatePacAsync(CancellationToken cancellationToken = default);

    Task<OrchestrationResult> UpdateListsAsync(CancellationToken cancellationToken = default);

    Task<OrchestrationResult> RecoverProxyStateAsync(CancellationToken cancellationToken = default);

    Task EmergencyRollbackAsync(CancellationToken cancellationToken = default);
}
