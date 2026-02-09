using System.Text.Json.Serialization;

namespace VibeShadowsocks.Core.Models;

public sealed record HotkeyGesture
{
    public bool Ctrl { get; init; } = true;

    public bool Alt { get; init; } = true;

    public bool Shift { get; init; } = true;

    public bool Win { get; init; }

    public string Key { get; init; } = "P";

    [JsonIgnore]
    public string Display => string.Join("+",
        new[]
        {
            Ctrl ? "Ctrl" : null,
            Alt ? "Alt" : null,
            Shift ? "Shift" : null,
            Win ? "Win" : null,
            Key.ToUpperInvariant(),
        }.Where(value => !string.IsNullOrWhiteSpace(value))!);
}
