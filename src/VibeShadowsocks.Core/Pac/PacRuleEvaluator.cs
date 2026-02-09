using System.Text.RegularExpressions;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Pac;

public static class PacRuleEvaluator
{
    public static PacEvaluationResult Evaluate(PacRuleSet rules, string input, int socksPort, PacDefaultAction defaultAction)
    {
        var normalizedInput = NormalizeInput(input, out var host);
        var proxyDirective = $"SOCKS5 127.0.0.1:{socksPort}";

        if (rules.BypassSimpleHostnames && !host.Contains('.', StringComparison.Ordinal))
        {
            return new PacEvaluationResult(input, host, "DIRECT", "plain hostname bypass");
        }

        if (rules.BypassPrivateAddresses && IsPrivateHost(host))
        {
            return new PacEvaluationResult(input, host, "DIRECT", "private network bypass");
        }

        if (rules.DirectDomains.Any(domain => DomainMatches(host, domain)))
        {
            return new PacEvaluationResult(input, host, "DIRECT", "direct domain rule");
        }

        if (rules.ProxyDomains.Any(domain => DomainMatches(host, domain)))
        {
            return new PacEvaluationResult(input, host, proxyDirective, "proxy domain rule");
        }

        if (rules.DirectGlobs.Any(pattern => GlobMatches(pattern, host) || GlobMatches(pattern, normalizedInput)))
        {
            return new PacEvaluationResult(input, host, "DIRECT", "direct glob rule");
        }

        if (rules.ProxyGlobs.Any(pattern => GlobMatches(pattern, host) || GlobMatches(pattern, normalizedInput)))
        {
            return new PacEvaluationResult(input, host, proxyDirective, "proxy glob rule");
        }

        if (rules.DirectRegex.Any(pattern => RegexMatches(pattern, host) || RegexMatches(pattern, normalizedInput)))
        {
            return new PacEvaluationResult(input, host, "DIRECT", "direct regex rule");
        }

        if (rules.ProxyRegex.Any(pattern => RegexMatches(pattern, host) || RegexMatches(pattern, normalizedInput)))
        {
            return new PacEvaluationResult(input, host, proxyDirective, "proxy regex rule");
        }

        return defaultAction == PacDefaultAction.Proxy
            ? new PacEvaluationResult(input, host, proxyDirective, "default proxy")
            : new PacEvaluationResult(input, host, "DIRECT", "default direct");
    }

    private static string NormalizeInput(string input, out string host)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            return uri.ToString();
        }

        host = input.Trim();
        return $"http://{host.TrimStart('/')}";
    }

    private static bool DomainMatches(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);

    private static bool GlobMatches(string pattern, string input)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }

    private static bool RegexMatches(string pattern, string input)
    {
        return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }

    private static bool IsPrivateHost(string host)
    {
        if (!System.Net.IPAddress.TryParse(host, out var ip))
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4 &&
            (bytes[0] == 10 ||
             (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
             (bytes[0] == 192 && bytes[1] == 168) ||
             bytes[0] == 127);
    }
}
