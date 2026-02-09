using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Infrastructure.Options;

namespace VibeShadowsocks.App.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly AppPaths _paths;

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public LogsViewModel(IDiagnosticsService diagnosticsService, AppPaths paths)
    {
        _diagnosticsService = diagnosticsService;
        _paths = paths;
    }

    public async Task LoadAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!Directory.Exists(_paths.LogsDirectory))
        {
            LogText = "No log directory yet.";
            StatusMessage = string.Empty;
            return;
        }

        var latestLogFile = Directory
            .EnumerateFiles(_paths.LogsDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latestLogFile is null)
        {
            LogText = "No log files found.";
            StatusMessage = string.Empty;
            return;
        }

        LogText = await File.ReadAllTextAsync(latestLogFile);
        StatusMessage = latestLogFile;
    }

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var outputPath = await _diagnosticsService.ExportBundleAsync(desktop);
        StatusMessage = $"Diagnostics exported: {outputPath}";
    }
}
