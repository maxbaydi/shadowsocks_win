namespace VibeShadowsocks.Core.Models;

public sealed record LocalPortSettings
{
    public int SocksPort { get; init; } = 1080;

    public int HttpPort { get; init; } = 1081;

    public int PacServerPort { get; init; } = 8090;

    public string ListenAddress { get; init; } = "127.0.0.1";

    public bool AutoSelectOnConflict { get; init; } = true;
}
