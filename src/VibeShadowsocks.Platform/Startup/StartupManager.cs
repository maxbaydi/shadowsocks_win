using Microsoft.Win32;

namespace VibeShadowsocks.Platform.Startup;

public sealed class StartupManager : IStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VibeShadowsocks";

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName)?.ToString();
        return Task.FromResult(!string.IsNullOrWhiteSpace(value));
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Cannot open startup registry key.");

        if (enabled)
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                processPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(processPath))
            {
                throw new InvalidOperationException("Unable to determine executable path for startup registration.");
            }

            key.SetValue(ValueName, $"\"{processPath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        return Task.CompletedTask;
    }
}
