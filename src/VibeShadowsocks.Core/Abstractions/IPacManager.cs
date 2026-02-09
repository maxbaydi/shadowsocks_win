using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Core.Pac;

namespace VibeShadowsocks.Core.Abstractions;

public interface IPacManager
{
    Task StartAsync(int port, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<PacUpdateResult> UpdateManagedPacAsync(PacProfile profile, int socksPort, CancellationToken cancellationToken = default);

    Task<Uri> ResolvePacUriAsync(PacProfile profile, int socksPort, int pacPort, CancellationToken cancellationToken = default);

    string GetPacPreview();

    PacEvaluationResult TestRule(PacProfile profile, string urlOrHost, int socksPort);

    Task<IReadOnlyList<PacPreset>> GetPresetsAsync(CancellationToken cancellationToken = default);
}

public sealed record PacUpdateResult(bool Applied, string Message, PacRuleSet? Rules);

public sealed record PacEvaluationResult(string Input, string Host, string Decision, string Reason);
