using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Infrastructure.Interop;
using VibeShadowsocks.Infrastructure.Options;
using VibeShadowsocks.Infrastructure.Storage;

namespace VibeShadowsocks.Infrastructure.Proxy;

public sealed class SystemProxyManager(ILogger<SystemProxyManager> logger, AppPaths paths) : ISystemProxyManager
{
    private const string InternetSettingsKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private readonly ILogger<SystemProxyManager> _logger = logger;
    private readonly AppPaths _paths = paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ProxySnapshot? _activeSnapshot;
    private bool _sessionStarted;

    public async Task CrashRecoverIfNeededAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recoveryPath = GetRecoveryPath();
            if (!File.Exists(recoveryPath))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(recoveryPath, cancellationToken).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize<ProxySnapshot>(json);
            if (snapshot is null)
            {
                _logger.LogWarning("Proxy recovery file exists but cannot be parsed.");
                File.Delete(recoveryPath);
                return;
            }

            _logger.LogWarning("Detected dirty proxy state. Running crash recovery.");
            ApplySnapshot(snapshot);
            WinInetNative.RefreshInternetSettings();
            File.Delete(recoveryPath);
            _activeSnapshot = null;
            _sessionStarted = false;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Crash recovery failed.");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BeginSessionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionStarted)
            {
                return;
            }

            var snapshot = CaptureSnapshot();
            _activeSnapshot = snapshot;
            _sessionStarted = true;

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await AtomicFileWriter.WriteTextAsync(GetRecoveryPath(), json, cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("System proxy transaction started.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyRoutingModeAsync(RoutingMode routingMode, int socksPort, Uri? pacUri, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (routingMode != RoutingMode.Off && !_sessionStarted)
            {
                throw new InvalidOperationException("Proxy session is not started. Call BeginSessionAsync first.");
            }

            using var key = OpenSettingsKey(writable: true);
            switch (routingMode)
            {
                case RoutingMode.Off:
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    key.DeleteValue("ProxyServer", throwOnMissingValue: false);
                    key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
                    key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
                    break;
                case RoutingMode.Global:
                    key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    key.SetValue("ProxyServer", $"socks=127.0.0.1:{socksPort}", RegistryValueKind.String);
                    key.SetValue("ProxyOverride", "<local>;127.*;localhost", RegistryValueKind.String);
                    key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
                    key.SetValue("AutoDetect", 0, RegistryValueKind.DWord);
                    break;
                case RoutingMode.Pac:
                    if (pacUri is null)
                    {
                        throw new InvalidOperationException("PAC URI is required for PAC mode.");
                    }

                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    key.DeleteValue("ProxyServer", throwOnMissingValue: false);
                    key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
                    key.SetValue("AutoConfigURL", pacUri.ToString(), RegistryValueKind.String);
                    key.SetValue("AutoDetect", 0, RegistryValueKind.DWord);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(routingMode), routingMode, "Unsupported routing mode.");
            }

            WinInetNative.RefreshInternetSettings();
            _logger.LogInformation("System proxy mode applied: {RoutingMode}", routingMode);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to apply proxy mode {RoutingMode}", routingMode);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = _activeSnapshot ?? await LoadRecoverySnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                _logger.LogInformation("Rollback skipped: no active proxy snapshot.");
                return;
            }

            ApplySnapshot(snapshot);
            WinInetNative.RefreshInternetSettings();
            await ClearRecoveryStateAsync().ConfigureAwait(false);

            _activeSnapshot = null;
            _sessionStarted = false;
            _logger.LogInformation("System proxy rollback complete.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "System proxy rollback failed.");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ProxySnapshot CaptureSnapshot()
    {
        using var key = OpenSettingsKey(writable: false);
        return new ProxySnapshot
        {
            ProxyEnable = Convert.ToInt32(key.GetValue("ProxyEnable", 0)),
            ProxyServer = key.GetValue("ProxyServer")?.ToString(),
            ProxyOverride = key.GetValue("ProxyOverride")?.ToString(),
            AutoConfigUrl = key.GetValue("AutoConfigURL")?.ToString(),
            AutoDetect = Convert.ToInt32(key.GetValue("AutoDetect", 0)),
            CapturedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private void ApplySnapshot(ProxySnapshot snapshot)
    {
        using var key = OpenSettingsKey(writable: true);

        key.SetValue("ProxyEnable", snapshot.ProxyEnable, RegistryValueKind.DWord);
        SetOrDeleteString(key, "ProxyServer", snapshot.ProxyServer);
        SetOrDeleteString(key, "ProxyOverride", snapshot.ProxyOverride);
        SetOrDeleteString(key, "AutoConfigURL", snapshot.AutoConfigUrl);
        key.SetValue("AutoDetect", snapshot.AutoDetect, RegistryValueKind.DWord);
    }

    private static void SetOrDeleteString(RegistryKey key, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }

    private static RegistryKey OpenSettingsKey(bool writable)
    {
        return Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable)
            ?? throw new InvalidOperationException("Cannot open Internet Settings registry key.");
    }

    private string GetRecoveryPath()
    {
        Directory.CreateDirectory(_paths.StateDirectory);
        return Path.Combine(_paths.StateDirectory, "proxy_snapshot.json");
    }

    private async Task<ProxySnapshot?> LoadRecoverySnapshotAsync(CancellationToken cancellationToken)
    {
        var path = GetRecoveryPath();
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ProxySnapshot>(json);
    }

    private Task ClearRecoveryStateAsync()
    {
        var path = GetRecoveryPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
