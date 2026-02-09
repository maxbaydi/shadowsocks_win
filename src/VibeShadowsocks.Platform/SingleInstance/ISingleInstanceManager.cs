namespace VibeShadowsocks.Platform.SingleInstance;

public interface ISingleInstanceManager : IDisposable
{
    event EventHandler? ActivationRequested;

    bool IsPrimaryInstance { get; }

    Task<bool> InitializeAsync(string instanceId, CancellationToken cancellationToken = default);
}
