namespace VibeShadowsocks.Infrastructure.Options;

public sealed record AppPaths
{
    public required string RootDirectory { get; init; }

    public required string SettingsPath { get; init; }

    public required string LogsDirectory { get; init; }

    public required string CacheDirectory { get; init; }

    public required string RuntimeDirectory { get; init; }

    public required string StateDirectory { get; init; }

    public required string SecretsDirectory { get; init; }

    public static AppPaths CreateDefault()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VibeShadowsocks");

        return new AppPaths
        {
            RootDirectory = root,
            SettingsPath = Path.Combine(root, "settings.json"),
            LogsDirectory = Path.Combine(root, "logs"),
            CacheDirectory = Path.Combine(root, "cache"),
            RuntimeDirectory = Path.Combine(root, "runtime"),
            StateDirectory = Path.Combine(root, "state"),
            SecretsDirectory = Path.Combine(root, "secrets"),
        };
    }
}
