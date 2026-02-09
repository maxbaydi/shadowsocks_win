namespace VibeShadowsocks.Infrastructure.Proxy;

public sealed record ProxySnapshot
{
    public int ProxyEnable { get; init; }

    public string? ProxyServer { get; init; }

    public string? ProxyOverride { get; init; }

    public string? AutoConfigUrl { get; init; }

    public int AutoDetect { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
