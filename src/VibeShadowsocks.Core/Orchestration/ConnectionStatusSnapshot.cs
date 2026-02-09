namespace VibeShadowsocks.Core.Orchestration;

public sealed record ConnectionStatusSnapshot(
    ConnectionState State,
    string Message,
    DateTimeOffset UpdatedAtUtc,
    string? ActiveProfileId,
    string? ActivePacProfileId,
    int? ProcessId);
