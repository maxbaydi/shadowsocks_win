using System.Text;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Pac;

public static class PacScriptGenerator
{
    public static string Generate(PacRuleSet rules, int socksPort, PacDefaultAction defaultAction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(socksPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(socksPort, 65535);

        var proxyDirective = $"SOCKS5 127.0.0.1:{socksPort}";
        var defaultDirective = defaultAction == PacDefaultAction.Proxy ? proxyDirective : "DIRECT";

        var builder = new StringBuilder();
        builder.AppendLine("function dnsDomainIsAny(host, domains) {");
        builder.AppendLine("  for (var i = 0; i < domains.length; i++) {");
        builder.AppendLine("    var d = domains[i];");
        builder.AppendLine("    if (host === d || dnsDomainIs(host, '.' + d)) return true;");
        builder.AppendLine("  }");
        builder.AppendLine("  return false;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("function shExpAnyMatch(input, patterns) {");
        builder.AppendLine("  for (var i = 0; i < patterns.length; i++) {");
        builder.AppendLine("    if (shExpMatch(input, patterns[i])) return true;");
        builder.AppendLine("  }");
        builder.AppendLine("  return false;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("function regexAnyMatch(input, regexValues) {");
        builder.AppendLine("  for (var i = 0; i < regexValues.length; i++) {");
        builder.AppendLine("    var rx = new RegExp(regexValues[i]);");
        builder.AppendLine("    if (rx.test(input)) return true;");
        builder.AppendLine("  }");
        builder.AppendLine("  return false;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine($"var DIRECT_DOMAINS = [{JoinQuoted(rules.DirectDomains)}];");
        builder.AppendLine($"var PROXY_DOMAINS = [{JoinQuoted(rules.ProxyDomains)}];");
        builder.AppendLine($"var DIRECT_GLOBS = [{JoinQuoted(rules.DirectGlobs)}];");
        builder.AppendLine($"var PROXY_GLOBS = [{JoinQuoted(rules.ProxyGlobs)}];");
        builder.AppendLine($"var DIRECT_REGEX = [{JoinQuoted(rules.DirectRegex)}];");
        builder.AppendLine($"var PROXY_REGEX = [{JoinQuoted(rules.ProxyRegex)}];");
        builder.AppendLine();
        builder.AppendLine("function FindProxyForURL(url, host) {");

        if (rules.BypassSimpleHostnames)
        {
            builder.AppendLine("  if (isPlainHostName(host)) return 'DIRECT';");
        }

        if (rules.BypassPrivateAddresses)
        {
            builder.AppendLine("  if (isInNet(host, '10.0.0.0', '255.0.0.0')) return 'DIRECT';");
            builder.AppendLine("  if (isInNet(host, '172.16.0.0', '255.240.0.0')) return 'DIRECT';");
            builder.AppendLine("  if (isInNet(host, '192.168.0.0', '255.255.0.0')) return 'DIRECT';");
            builder.AppendLine("  if (isInNet(host, '127.0.0.0', '255.0.0.0')) return 'DIRECT';");
        }

        builder.AppendLine("  if (dnsDomainIsAny(host, DIRECT_DOMAINS)) return 'DIRECT';");
        builder.AppendLine($"  if (dnsDomainIsAny(host, PROXY_DOMAINS)) return '{proxyDirective}';");
        builder.AppendLine("  if (shExpAnyMatch(host, DIRECT_GLOBS) || shExpAnyMatch(url, DIRECT_GLOBS)) return 'DIRECT';");
        builder.AppendLine($"  if (shExpAnyMatch(host, PROXY_GLOBS) || shExpAnyMatch(url, PROXY_GLOBS)) return '{proxyDirective}';");
        builder.AppendLine("  if (regexAnyMatch(host, DIRECT_REGEX) || regexAnyMatch(url, DIRECT_REGEX)) return 'DIRECT';");
        builder.AppendLine($"  if (regexAnyMatch(host, PROXY_REGEX) || regexAnyMatch(url, PROXY_REGEX)) return '{proxyDirective}';");
        builder.AppendLine($"  return '{defaultDirective}';");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string JoinQuoted(IEnumerable<string> values) => string.Join(",", values.Select(value => $"'{value.Replace("'", "\\'")}'"));
}
