namespace VibeShadowsocks.Core.Models;

public sealed record UpdateInfo(
    string TargetVersion,
    long SizeBytes,
    string? ReleaseNotes);
