namespace VibeShadowsocks.Core.Models;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? SsLocalExecutablePath { get; init; }

    public bool AutoStart { get; init; }

    public bool AutoConnect { get; init; }

    public bool MinimizeToTrayOnClose { get; init; } = true;

    public HotkeyGesture Hotkey { get; init; } = new();

    public LocalPortSettings Ports { get; init; } = new();

    public RoutingMode RoutingMode { get; init; } = RoutingMode.Off;

    public string? ActiveServerProfileId { get; init; }

    public string? ActivePacProfileId { get; init; }

    public List<ServerProfile> ServerProfiles { get; init; } = [];

    public List<PacProfile> PacProfiles { get; init; } = [];

    public string LogLevel { get; init; } = "Information";

    public int DiagnosticsLogTailLines { get; init; } = 300;

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public ServerProfile? GetActiveServerProfile() =>
        ServerProfiles.FirstOrDefault(profile => profile.Id == ActiveServerProfileId);

    public PacProfile? GetActivePacProfile() =>
        PacProfiles.FirstOrDefault(profile => profile.Id == ActivePacProfileId);
}
