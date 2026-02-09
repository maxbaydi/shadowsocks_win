namespace VibeShadowsocks.Core.Pac;

public sealed record PacRuleSet
{
    public IReadOnlyList<string> DirectDomains { get; init; } = [];

    public IReadOnlyList<string> ProxyDomains { get; init; } = [];

    public IReadOnlyList<string> DirectGlobs { get; init; } = [];

    public IReadOnlyList<string> ProxyGlobs { get; init; } = [];

    public IReadOnlyList<string> DirectRegex { get; init; } = [];

    public IReadOnlyList<string> ProxyRegex { get; init; } = [];

    public bool BypassPrivateAddresses { get; init; } = true;

    public bool BypassSimpleHostnames { get; init; } = true;
}
