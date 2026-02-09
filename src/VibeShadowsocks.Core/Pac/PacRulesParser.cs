using System.Text.RegularExpressions;

namespace VibeShadowsocks.Core.Pac;

public static class PacRulesParser
{
    public static PacRuleSet Parse(string rawText, bool bypassPrivateAddresses = true, bool bypassSimpleHostnames = true)
    {
        var directDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proxyDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directGlobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proxyGlobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directRegex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proxyRegex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceLine in rawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = sourceLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith('!') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("@@||", StringComparison.Ordinal))
            {
                AddDomain(directDomains, line[4..]);
                continue;
            }

            if (line.StartsWith("||", StringComparison.Ordinal))
            {
                AddDomain(proxyDomains, line[2..]);
                continue;
            }

            if (line.StartsWith("DIRECT ", StringComparison.OrdinalIgnoreCase))
            {
                AddByKind(directDomains, directGlobs, directRegex, line[7..]);
                continue;
            }

            if (line.StartsWith("PROXY ", StringComparison.OrdinalIgnoreCase))
            {
                AddByKind(proxyDomains, proxyGlobs, proxyRegex, line[6..]);
                continue;
            }

            AddByKind(proxyDomains, proxyGlobs, proxyRegex, line);
        }

        return new PacRuleSet
        {
            DirectDomains = directDomains.OrderBy(value => value).ToArray(),
            ProxyDomains = proxyDomains.OrderBy(value => value).ToArray(),
            DirectGlobs = directGlobs.OrderBy(value => value).ToArray(),
            ProxyGlobs = proxyGlobs.OrderBy(value => value).ToArray(),
            DirectRegex = directRegex.OrderBy(value => value).ToArray(),
            ProxyRegex = proxyRegex.OrderBy(value => value).ToArray(),
            BypassPrivateAddresses = bypassPrivateAddresses,
            BypassSimpleHostnames = bypassSimpleHostnames,
        };
    }

    private static void AddByKind(
        HashSet<string> domains,
        HashSet<string> globs,
        HashSet<string> regexes,
        string rawToken)
    {
        var token = rawToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (token.StartsWith("/", StringComparison.Ordinal) && token.EndsWith("/", StringComparison.Ordinal) && token.Length > 2)
        {
            var value = token[1..^1];
            _ = Regex.IsMatch(string.Empty, value);
            regexes.Add(value);
            return;
        }

        if (token.Contains('*', StringComparison.Ordinal))
        {
            globs.Add(token);
            return;
        }

        AddDomain(domains, token);
    }

    private static void AddDomain(HashSet<string> domains, string value)
    {
        var normalized = value.Trim().TrimStart('.').TrimEnd('/').Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            domains.Add(normalized);
        }
    }
}
