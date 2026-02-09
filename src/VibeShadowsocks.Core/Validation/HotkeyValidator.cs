using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Validation;

public static class HotkeyValidator
{
    public static IReadOnlyList<string> Validate(HotkeyGesture hotkey)
    {
        var errors = new List<string>();

        if (!hotkey.Ctrl && !hotkey.Alt && !hotkey.Shift && !hotkey.Win)
        {
            errors.Add("At least one hotkey modifier is required.");
        }

        if (string.IsNullOrWhiteSpace(hotkey.Key) || hotkey.Key.Length > 16)
        {
            errors.Add("Hotkey key is invalid.");
        }

        return errors;
    }
}
