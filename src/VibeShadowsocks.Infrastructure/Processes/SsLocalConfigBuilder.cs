using System.Text.Json;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Infrastructure.Processes;

public static class SsLocalConfigBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static string BuildConfigJson(ServerProfile profile, string password, int socksPort)
    {
        var config = new
        {
            servers = new[]
            {
                new
                {
                    address = profile.Host,
                    port = profile.Port,
                    method = profile.Method,
                    password,
                    plugin = profile.Plugin,
                    plugin_opts = profile.PluginOptions,
                },
            },
            locals = new[]
            {
                new
                {
                    local_address = "127.0.0.1",
                    local_port = socksPort,
                    protocol = "socks",
                },
            },
        };

        return JsonSerializer.Serialize(config, SerializerOptions);
    }

    public static string BuildServerUrl(ServerProfile profile, string password)
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{profile.Method}:{password}"));
        var normalized = payload.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"ss://{normalized}@{profile.Host}:{profile.Port}";
    }
}
