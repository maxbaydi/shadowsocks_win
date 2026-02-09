namespace VibeShadowsocks.Core.Abstractions;

public interface IPortAvailabilityProbe
{
    Task<bool> IsPortAvailableAsync(int port, CancellationToken cancellationToken = default);

    Task<int> FindAvailablePortAsync(int preferredPort, CancellationToken cancellationToken = default);
}
