using System.Text;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Core.Pac;
using VibeShadowsocks.Infrastructure.Networking;

namespace VibeShadowsocks.Infrastructure.Pac;

public sealed class PacManager(
    ILogger<PacManager> logger,
    LocalPacHttpServer localPacHttpServer,
    HttpCache httpCache,
    GitHubPresetProvider presetProvider) : IPacManager
{
    private readonly ILogger<PacManager> _logger = logger;
    private readonly LocalPacHttpServer _localPacHttpServer = localPacHttpServer;
    private readonly HttpCache _httpCache = httpCache;
    private readonly GitHubPresetProvider _presetProvider = presetProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PacRuleSet _lastGoodRules = new();
    private string _lastGoodPacScript = "function FindProxyForURL(url, host) { return 'DIRECT'; }";

    public Task StartAsync(int port, CancellationToken cancellationToken = default)
        => _localPacHttpServer.StartAsync(port, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _localPacHttpServer.StopAsync(cancellationToken);

    public async Task<PacUpdateResult> UpdateManagedPacAsync(PacProfile profile, int socksPort, CancellationToken cancellationToken = default)
    {
        if (profile.Type != PacProfileType.Managed)
        {
            return new PacUpdateResult(false, "PAC profile is not managed.", null);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mergedRulesText = await LoadManagedRulesAsync(profile, cancellationToken).ConfigureAwait(false);
            var parsedRules = PacRulesParser.Parse(
                mergedRulesText,
                bypassPrivateAddresses: profile.BypassPrivateAddresses,
                bypassSimpleHostnames: profile.BypassSimpleHostnames);

            var pacScript = PacScriptGenerator.Generate(parsedRules, socksPort, profile.DefaultAction);
            _lastGoodRules = parsedRules;
            _lastGoodPacScript = pacScript;

            if (_localPacHttpServer.IsRunning)
            {
                await _localPacHttpServer.UpdatePacScriptAsync(pacScript, cancellationToken).ConfigureAwait(false);
            }

            return new PacUpdateResult(true, "Managed PAC updated.", parsedRules);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Managed PAC update failed. Keeping previous PAC version.");
            return new PacUpdateResult(false, $"Managed PAC update failed: {exception.Message}", _lastGoodRules);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Uri> ResolvePacUriAsync(PacProfile profile, int socksPort, int pacPort, CancellationToken cancellationToken = default)
    {
        if (profile.Type == PacProfileType.Remote)
        {
            if (string.IsNullOrWhiteSpace(profile.RemotePacUrl))
            {
                throw new InvalidOperationException("Remote PAC URL is empty.");
            }

            var remoteUri = new Uri(profile.RemotePacUrl, UriKind.Absolute);
            if (!remoteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Remote PAC must use HTTPS.");
            }

            return remoteUri;
        }

        await StartAsync(pacPort, cancellationToken).ConfigureAwait(false);

        if (_lastGoodRules.ProxyDomains.Count == 0 && _lastGoodRules.ProxyGlobs.Count == 0 && _lastGoodRules.ProxyRegex.Count == 0)
        {
            var updateResult = await UpdateManagedPacAsync(profile, socksPort, cancellationToken).ConfigureAwait(false);
            if (!updateResult.Applied)
            {
                throw new InvalidOperationException(updateResult.Message);
            }
        }
        else
        {
            await _localPacHttpServer.UpdatePacScriptAsync(_lastGoodPacScript, cancellationToken).ConfigureAwait(false);
        }

        return _localPacHttpServer.CurrentUri;
    }

    public string GetPacPreview() => _lastGoodPacScript;

    public PacEvaluationResult TestRule(PacProfile profile, string urlOrHost, int socksPort)
    {
        if (profile.Type == PacProfileType.Remote)
        {
            return new PacEvaluationResult(urlOrHost, urlOrHost, "N/A", "Remote PAC cannot be evaluated locally.");
        }

        return PacRuleEvaluator.Evaluate(_lastGoodRules, urlOrHost, socksPort, profile.DefaultAction);
    }

    public Task<IReadOnlyList<PacPreset>> GetPresetsAsync(CancellationToken cancellationToken = default)
        => _presetProvider.GetPresetsAsync(cancellationToken: cancellationToken);

    private async Task<string> LoadManagedRulesAsync(PacProfile profile, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(profile.InlineRules))
        {
            builder.AppendLine(profile.InlineRules);
        }

        if (!string.IsNullOrWhiteSpace(profile.LocalRulesFilePath) && File.Exists(profile.LocalRulesFilePath))
        {
            var localRules = await File.ReadAllTextAsync(profile.LocalRulesFilePath, cancellationToken).ConfigureAwait(false);
            builder.AppendLine(localRules);
        }

        if (!string.IsNullOrWhiteSpace(profile.RulesUrl))
        {
            var uri = new Uri(profile.RulesUrl, UriKind.Absolute);
            var remoteRules = await _httpCache
                .GetStringAsync(uri, $"pac-rules:{uri}", maxBytes: 3_000_000, cancellationToken)
                .ConfigureAwait(false);
            builder.AppendLine(remoteRules.Content);
        }

        return builder.ToString();
    }
}
