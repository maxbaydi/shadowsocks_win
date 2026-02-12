using System.Text.Json;
using System.Text.Json.Nodes;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Infrastructure.Processes;

public static class SsLocalConfigBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static string BuildConfigJson(ServerProfile profile, string password, int socksPort, int httpPort, string listenAddress)
    {
        var locals = new JsonArray
        {
            new JsonObject
            {
                ["local_address"] = listenAddress,
                ["local_port"] = socksPort,
                ["protocol"] = "socks",
            },
        };

        if (httpPort > 0)
        {
            locals.Add(new JsonObject
            {
                ["local_address"] = listenAddress,
                ["local_port"] = httpPort,
                ["protocol"] = "http",
            });
        }

        var config = new JsonObject
        {
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["address"] = profile.Host,
                    ["port"] = profile.Port,
                    ["method"] = profile.Method,
                    ["password"] = password,
                    ["plugin"] = profile.Plugin is not null ? JsonValue.Create(profile.Plugin) : null,
                    ["plugin_opts"] = profile.PluginOptions is not null ? JsonValue.Create(profile.PluginOptions) : null,
                },
            },
            ["locals"] = locals,
        };

        return config.ToJsonString(SerializerOptions);
    }

    public static string BuildServerUrl(ServerProfile profile, string password)
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{profile.Method}:{password}"));
        var normalized = payload.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"ss://{normalized}@{profile.Host}:{profile.Port}";
    }
}
