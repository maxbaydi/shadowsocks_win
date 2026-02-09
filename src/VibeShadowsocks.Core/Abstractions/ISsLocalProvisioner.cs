namespace VibeShadowsocks.Core.Abstractions;

public interface ISsLocalProvisioner
{
    Task<string> EnsureAvailableAsync(string? configuredPath, CancellationToken ct = default);
}
