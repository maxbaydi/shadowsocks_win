using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.Core.Abstractions;

public interface ISettingsValidator
{
    IReadOnlyList<string> Validate(AppSettings settings);

    IReadOnlyList<string> ValidateProfile(ServerProfile profile);
}
