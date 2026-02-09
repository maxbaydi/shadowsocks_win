namespace VibeShadowsocks.Core.Models;

public sealed record DiagnosticsSnapshot
{
    public string AppVersion { get; init; } = string.Empty;

    public string OsVersion { get; init; } = string.Empty;

    public string RuntimeVersion { get; init; } = string.Empty;

    public string CurrentState { get; init; } = string.Empty;

    public string ActiveProfileId { get; init; } = string.Empty;

    public string ActivePacProfileId { get; init; } = string.Empty;

    public int SocksPort { get; init; }

    public int PacPort { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<string> RecentLogLines { get; init; } = [];
}
