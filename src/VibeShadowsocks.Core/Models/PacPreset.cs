namespace VibeShadowsocks.Core.Models;

public sealed record PacPreset
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;
}
