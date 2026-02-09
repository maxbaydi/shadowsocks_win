namespace VibeShadowsocks.Core.Models;

public sealed record LocalPortSettings
{
    public int SocksPort { get; init; } = 1080;

    public int PacServerPort { get; init; } = 8090;

    public bool AutoSelectOnConflict { get; init; } = true;
}
