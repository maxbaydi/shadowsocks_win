namespace VibeShadowsocks.Core.Models;

public sealed record ServerProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "New Server";

    public string Group { get; init; } = string.Empty;

    public List<string> Tags { get; init; } = [];

    public bool IsFavorite { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 8388;

    public string Method { get; init; } = "aes-256-gcm";

    public string PasswordSecretId { get; init; } = Guid.NewGuid().ToString("N");

    public string? Plugin { get; init; }

    public string? PluginOptions { get; init; }

    public string? Remarks { get; init; }

    public static ServerProfile CreateDefault(string name) => new()
    {
        Name = name,
    };
}
