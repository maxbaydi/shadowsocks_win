namespace VibeShadowsocks.Core.Models;

public sealed record PacProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "Default PAC";

    public PacProfileType Type { get; init; } = PacProfileType.Managed;

    public string? RemotePacUrl { get; init; }

    public string? RulesUrl { get; init; }

    public string? LocalRulesFilePath { get; init; }

    public string InlineRules { get; init; } = string.Empty;

    public PacDefaultAction DefaultAction { get; init; } = PacDefaultAction.Proxy;

    public int UpdateIntervalHours { get; init; } = 6;

    public bool BypassPrivateAddresses { get; init; } = true;

    public bool BypassSimpleHostnames { get; init; } = true;
}
