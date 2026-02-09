using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Abstractions;

public interface IUpdateService
{
    string? CurrentVersion { get; }

    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);

    Task DownloadUpdateAsync(Action<int>? progress = null, CancellationToken ct = default);

    void ApplyUpdateAndRestart();
}
