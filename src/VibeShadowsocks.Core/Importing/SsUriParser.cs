using System.Net;
using System.Text;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Importing;

public static class SsUriParser
{
    public static ServerProfile Parse(string ssUri, out string password)
    {
        if (string.IsNullOrWhiteSpace(ssUri))
        {
            throw new ArgumentException("ss uri is empty", nameof(ssUri));
        }

        if (!ssUri.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("URI must start with ss://");
        }

        var body = ssUri[5..];
        var fragmentSplit = body.Split('#', 2);
        var beforeFragment = fragmentSplit[0];
        var remarks = fragmentSplit.Length > 1 ? Uri.UnescapeDataString(fragmentSplit[1]) : "Imported";

        var querySplit = beforeFragment.Split('?', 2);
        var endpointPart = querySplit[0];
        var query = querySplit.Length > 1 ? querySplit[1] : string.Empty;

        string userInfoPart;
        string hostPart;

        if (endpointPart.Contains('@', StringComparison.Ordinal))
        {
            var atIndex = endpointPart.LastIndexOf('@');
            userInfoPart = endpointPart[..atIndex];
            hostPart = endpointPart[(atIndex + 1)..];
        }
        else
        {
            var decoded = DecodeBase64Url(endpointPart);
            var atIndex = decoded.LastIndexOf('@');
            if (atIndex <= 0)
            {
                throw new FormatException("Invalid SIP002 payload.");
            }

            userInfoPart = decoded[..atIndex];
            hostPart = decoded[(atIndex + 1)..];
        }

        var decodedUserInfo = userInfoPart.Contains(':', StringComparison.Ordinal)
            ? userInfoPart
            : DecodeBase64Url(userInfoPart);

        var methodPasswordSplit = decodedUserInfo.Split(':', 2);
        if (methodPasswordSplit.Length != 2)
        {
            throw new FormatException("Invalid method/password block.");
        }

        var hostPortSplit = hostPart.Split(':', 2);
        if (hostPortSplit.Length != 2 || !int.TryParse(hostPortSplit[1], out var port))
        {
            throw new FormatException("Invalid host/port block.");
        }

        var plugin = ParsePlugin(query);
        password = methodPasswordSplit[1];

        return new ServerProfile
        {
            Name = string.IsNullOrWhiteSpace(remarks) ? hostPortSplit[0] : remarks,
            Host = hostPortSplit[0],
            Port = port,
            Method = methodPasswordSplit[0],
            Plugin = plugin,
        };
    }

    public static string Export(ServerProfile profile, string password)
    {
        var userInfo = $"{profile.Method}:{password}@{profile.Host}:{profile.Port}";
        var encoded = EncodeBase64Url(userInfo);
        var remarks = Uri.EscapeDataString(profile.Name);
        var plugin = string.IsNullOrWhiteSpace(profile.Plugin)
            ? string.Empty
            : $"?plugin={Uri.EscapeDataString(profile.Plugin + (string.IsNullOrWhiteSpace(profile.PluginOptions) ? string.Empty : ";" + profile.PluginOptions))}";

        return $"ss://{encoded}{plugin}#{remarks}";
    }

    private static string? ParsePlugin(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("plugin", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }

    private static string DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2:
                normalized += "==";
                break;
            case 3:
                normalized += "=";
                break;
        }

        var bytes = Convert.FromBase64String(normalized);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string EncodeBase64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
