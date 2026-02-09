using System.Net;
using System.Net.Sockets;
using VibeShadowsocks.Core.Abstractions;

namespace VibeShadowsocks.Core.Validation;

public sealed class SocketPortAvailabilityProbe : IPortAvailabilityProbe
{
    public Task<bool> IsPortAvailableAsync(int port, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return Task.FromResult(true);
        }
        catch (SocketException)
        {
            return Task.FromResult(false);
        }
    }

    public async Task<int> FindAvailablePortAsync(int preferredPort, CancellationToken cancellationToken = default)
    {
        var candidate = preferredPort;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await IsPortAvailableAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                return candidate;
            }

            candidate++;
            if (candidate > 65535)
            {
                candidate = 1025;
            }
        }

        throw new InvalidOperationException("Unable to find an available TCP port.");
    }
}
