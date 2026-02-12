using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Abstractions;

namespace VibeShadowsocks.Infrastructure.Processes;

public sealed class SsLocalProvisioner : ISsLocalProvisioner
{
    private const string GitHubApiLatestRelease = "https://api.github.com/repos/shadowsocks/shadowsocks-rust/releases/latest";
    private const string AssetNamePattern = "x86_64-pc-windows-msvc";
    private const int DownloadTimeoutSeconds = 120;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ILogger<SsLocalProvisioner> _logger;

    public SsLocalProvisioner(ILogger<SsLocalProvisioner> logger)
    {
        _logger = logger;
    }

    public async Task<string> EnsureAvailableAsync(string? configuredPath, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var defaultPath = Path.Combine(AppContext.BaseDirectory, "tools", "sslocal", "sslocal.exe");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        await Gate.WaitAsync(ct);
        try
        {
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }

            _logger.LogInformation("sslocal.exe not found, downloading from shadowsocks-rust releases");
            await DownloadAsync(defaultPath, ct);
            return defaultPath;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task DownloadAsync(string targetPath, CancellationToken ct)
    {
        var handler = new System.Net.Http.HttpClientHandler { UseProxy = false };
        using var client = new System.Net.Http.HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(DownloadTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeShadowsocks/1.0");

        var releaseJson = await client.GetStringAsync(GitHubApiLatestRelease, ct);
        using var doc = JsonDocument.Parse(releaseJson);

        var assetUrl = FindWindowsAssetUrl(doc.RootElement);
        if (assetUrl is null)
        {
            throw new InvalidOperationException("Could not find Windows x64 asset in latest shadowsocks-rust release");
        }

        _logger.LogInformation("Downloading sslocal from {Url}", assetUrl);

        using var stream = await client.GetStreamAsync(assetUrl, ct);
        var tempZip = Path.GetTempFileName();
        try
        {
            await using (var fileStream = File.Create(tempZip))
            {
                await stream.CopyToAsync(fileStream, ct);
            }

            var targetDir = Path.GetDirectoryName(targetPath)!;
            Directory.CreateDirectory(targetDir);

            using var archive = ZipFile.OpenRead(tempZip);
            var ssLocalEntry = archive.Entries
                .FirstOrDefault(e => e.Name.Equals("sslocal.exe", StringComparison.OrdinalIgnoreCase));

            if (ssLocalEntry is null)
            {
                throw new InvalidOperationException("sslocal.exe not found inside the downloaded archive");
            }

            ssLocalEntry.ExtractToFile(targetPath, overwrite: true);
            _logger.LogInformation("sslocal.exe extracted to {Path}", targetPath);
        }
        finally
        {
            File.Delete(tempZip);
        }
    }

    private static string? FindWindowsAssetUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets))
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name))
            {
                continue;
            }

            var assetName = name.GetString() ?? string.Empty;
            if (assetName.Contains(AssetNamePattern, StringComparison.OrdinalIgnoreCase)
                && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return asset.GetProperty("browser_download_url").GetString();
            }
        }

        return null;
    }
}
