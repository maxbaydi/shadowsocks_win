using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Abstractions;

public interface ISystemProxyManager
{
    Task CrashRecoverIfNeededAsync(CancellationToken cancellationToken = default);

    Task BeginSessionAsync(CancellationToken cancellationToken = default);

    Task ApplyRoutingModeAsync(RoutingMode routingMode, int socksPort, Uri? pacUri, CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
