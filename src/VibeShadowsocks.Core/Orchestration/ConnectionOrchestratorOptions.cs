namespace VibeShadowsocks.Core.Orchestration;

public sealed record ConnectionOrchestratorOptions
{
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ProxyApplyTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan QueueShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
