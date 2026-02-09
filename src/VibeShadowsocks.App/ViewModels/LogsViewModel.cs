using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VibeShadowsocks.App.Helpers;
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

    public string TooltipRefreshLogs => Loc.Get("TooltipRefreshLogs");
    public string TooltipExportDiag => Loc.Get("TooltipExportDiag");

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
            LogText = Loc.Get("NoLogDirectory");
            StatusMessage = string.Empty;
            return;
        }

        var latestLogFile = Directory
            .EnumerateFiles(_paths.LogsDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latestLogFile is null)
        {
            LogText = Loc.Get("NoLogFiles");
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
        StatusMessage = Loc.Format("DiagnosticsExportedFmt", outputPath);
    }
}
