using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using UpdateInfo = VibeShadowsocks.Core.Models.UpdateInfo;

namespace VibeShadowsocks.Infrastructure.Updates;

public sealed class UpdateService : IUpdateService
{
    private const string GitHubRepoUrl = "https://github.com/maxbaydi/shadowsocks_win";
    private const int TimeoutSeconds = 30;

    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager _manager;
    private UpdateInfo? _pendingUpdate;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _manager = new UpdateManager(
            new GithubSource(GitHubRepoUrl, null, false),
            new UpdateOptions { AllowVersionDowngrade = false });
    }

    public string? CurrentVersion =>
        _manager.IsInstalled ? _manager.CurrentVersion?.ToString() : GetAssemblyVersion();

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled)
        {
            _logger.LogInformation("Not installed via Velopack, update check skipped");
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var result = await _manager.CheckForUpdatesAsync();
            if (result is null)
            {
                _logger.LogInformation("No updates available");
                return null;
            }

            var target = result.TargetFullRelease;
            _pendingUpdate = new UpdateInfo(
                target.Version.ToString(),
                target.Size,
                result.TargetFullRelease.NotesMarkdown);

            _logger.LogInformation("Update available: v{Version}, {Size} bytes",
                _pendingUpdate.TargetVersion, _pendingUpdate.SizeBytes);

            return _pendingUpdate;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Update check timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            return null;
        }
    }

    public async Task DownloadUpdateAsync(Action<int>? progress = null, CancellationToken ct = default)
    {
        if (!_manager.IsInstalled)
        {
            return;
        }

        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                return;
            }

            await _manager.DownloadUpdatesAsync(info, p => progress?.Invoke(p));
            _logger.LogInformation("Update downloaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update");
            throw;
        }
    }

    public void ApplyUpdateAndRestart()
    {
        if (!_manager.IsInstalled)
        {
            return;
        }

        try
        {
            _manager.ApplyUpdatesAndRestart(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update and restart");
            throw;
        }
    }

    private static string? GetAssemblyVersion()
    {
        var asm = typeof(UpdateService).Assembly.GetName().Version;
        return asm is not null ? $"{asm.Major}.{asm.Minor}.{asm.Build}" : null;
    }
}
