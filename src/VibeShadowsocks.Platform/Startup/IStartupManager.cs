namespace VibeShadowsocks.Platform.Startup;

public interface IStartupManager
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
