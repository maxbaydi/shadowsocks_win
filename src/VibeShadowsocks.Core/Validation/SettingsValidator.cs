using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Validation;

public sealed class SettingsValidator : ISettingsValidator
{
    public IReadOnlyList<string> Validate(AppSettings settings)
    {
        var errors = new List<string>();

        if (settings.Ports.SocksPort is < 1 or > 65535)
        {
            errors.Add("SOCKS port must be in range 1..65535.");
        }

        if (settings.Ports.PacServerPort is < 1 or > 65535)
        {
            errors.Add("PAC server port must be in range 1..65535.");
        }

        if (settings.Hotkey is null)
        {
            errors.Add("Hotkey is not configured.");
        }
        else
        {
            errors.AddRange(HotkeyValidator.Validate(settings.Hotkey));
        }

        if (!string.IsNullOrWhiteSpace(settings.ActiveServerProfileId) && settings.GetActiveServerProfile() is null)
        {
            errors.Add("Active server profile does not exist.");
        }

        if (settings.RoutingMode == RoutingMode.Pac && settings.GetActivePacProfile() is null)
        {
            errors.Add("PAC routing mode is enabled but active PAC profile is missing.");
        }

        foreach (var profile in settings.ServerProfiles)
        {
            errors.AddRange(ValidateProfile(profile));
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateProfile(ServerProfile profile)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add($"Profile {profile.Id} has empty name.");
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            errors.Add($"Profile {profile.Name} has empty host.");
        }

        if (profile.Port is < 1 or > 65535)
        {
            errors.Add($"Profile {profile.Name} has invalid server port.");
        }

        if (string.IsNullOrWhiteSpace(profile.Method))
        {
            errors.Add($"Profile {profile.Name} has empty cipher method.");
        }

        if (string.IsNullOrWhiteSpace(profile.PasswordSecretId))
        {
            errors.Add($"Profile {profile.Name} has no password secret id.");
        }

        return errors;
    }
}
