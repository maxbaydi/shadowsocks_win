using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Infrastructure.Networking;

namespace VibeShadowsocks.Infrastructure.Pac;

public sealed class GitHubPresetProvider(ILogger<GitHubPresetProvider> logger, HttpCache httpCache)
{
    private static readonly Uri DefaultIndexUri = new("https://raw.githubusercontent.com/shadowsocks/v2ray-rules-dat/release/pac/index.json");

    private readonly ILogger<GitHubPresetProvider> _logger = logger;
    private readonly HttpCache _httpCache = httpCache;

    public async Task<IReadOnlyList<PacPreset>> GetPresetsAsync(Uri? indexUri = null, CancellationToken cancellationToken = default)
    {
        var uri = indexUri ?? DefaultIndexUri;

        try
        {
            var cacheResult = await _httpCache
                .GetStringAsync(uri, $"pac-presets:{uri}", maxBytes: 1_500_000, cancellationToken)
                .ConfigureAwait(false);

            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var raw = JsonSerializer.Deserialize<List<PresetContract>>(cacheResult.Content, options);
            if (raw is null)
            {
                return [];
            }

            return raw
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.SourceUrl))
                .Select(item => new PacPreset
                {
                    Id = item.Id!,
                    Name = item.Name ?? item.Id!,
                    Description = item.Description ?? string.Empty,
                    SourceUrl = item.SourceUrl!,
                })
                .ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to fetch PAC presets from {Uri}", uri);
            return [];
        }
    }

    private sealed record PresetContract
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? Description { get; init; }

        public string? SourceUrl { get; init; }
    }
}
