using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Abstractions;

public interface ISsLocalRunner
{
    event EventHandler<SsLocalExitedEventArgs>? UnexpectedExit;

    bool IsRunning { get; }

    int? ProcessId { get; }

    Task<SsLocalStartResult> StartAsync(SsLocalStartRequest request, CancellationToken cancellationToken = default);

    Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default);

    Task<bool> WaitForLocalPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed record SsLocalStartRequest(
    string ExecutablePath,
    ServerProfile Profile,
    string Password,
    int SocksPort,
    string ConfigDirectory,
    bool UseServerUrlMode);

public sealed record SsLocalStartResult(int ProcessId, string ConfigPath, DateTimeOffset StartedAtUtc);

public sealed class SsLocalExitedEventArgs(int exitCode, string reason) : EventArgs
{
    public int ExitCode { get; } = exitCode;

    public string Reason { get; } = reason;
}
