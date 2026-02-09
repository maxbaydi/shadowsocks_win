using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.App.ViewModels;

public partial class RoutingViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IConnectionOrchestrator _orchestrator;
    private readonly IPacManager _pacManager;

    [ObservableProperty]
    private string _selectedRoutingMode = "Off";

    [ObservableProperty]
    private ObservableCollection<PacProfile> _pacProfiles = [];

    [ObservableProperty]
    private PacProfile? _selectedPacProfile;

    [ObservableProperty]
    private string _pacPreview = string.Empty;

    [ObservableProperty]
    private string _testInput = "https://example.com";

    [ObservableProperty]
    private string _testOutput = string.Empty;

    [ObservableProperty]
    private string _presetsSummary = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public IReadOnlyList<string> RoutingModes { get; } = ["Off", "Global", "Pac"];

    public RoutingViewModel(ISettingsStore settingsStore, IConnectionOrchestrator orchestrator, IPacManager pacManager)
    {
        _settingsStore = settingsStore;
        _orchestrator = orchestrator;
        _pacManager = pacManager;
    }

    public async Task LoadAsync()
    {
        var settings = await _settingsStore.LoadAsync();

        SelectedRoutingMode = settings.RoutingMode.ToString();
        PacProfiles = new ObservableCollection<PacProfile>(settings.PacProfiles);
        SelectedPacProfile = settings.GetActivePacProfile() ?? PacProfiles.FirstOrDefault();
        PacPreview = _pacManager.GetPacPreview();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!Enum.TryParse<RoutingMode>(SelectedRoutingMode, ignoreCase: true, out var routingMode))
        {
            StatusMessage = "Invalid routing mode value.";
            return;
        }

        await _settingsStore.UpdateAsync(settings => settings with
        {
            RoutingMode = routingMode,
            ActivePacProfileId = SelectedPacProfile?.Id,
        });

        var result = await _orchestrator.ApplyRoutingAsync();
        StatusMessage = result.Message;
    }

    [RelayCommand]
    private async Task UpdatePacAsync()
    {
        if (SelectedPacProfile is null)
        {
            StatusMessage = "Select PAC profile first.";
            return;
        }

        var settings = await _settingsStore.LoadAsync();
        var socksPort = settings.Ports.SocksPort;

        var updateResult = await _pacManager.UpdateManagedPacAsync(SelectedPacProfile, socksPort);
        PacPreview = _pacManager.GetPacPreview();
        StatusMessage = updateResult.Message;
    }

    [RelayCommand]
    private void Test()
    {
        if (SelectedPacProfile is null)
        {
            TestOutput = "No PAC profile selected.";
            return;
        }

        var result = _pacManager.TestRule(SelectedPacProfile, TestInput, 1080);
        TestOutput = $"Decision: {result.Decision}; Reason: {result.Reason}";
    }

    [RelayCommand]
    private async Task RefreshPresetsAsync()
    {
        var presets = await _pacManager.GetPresetsAsync();
        PresetsSummary = presets.Count == 0
            ? "No presets available."
            : string.Join(Environment.NewLine, presets.Select(preset => $"{preset.Name}: {preset.SourceUrl}"));
    }
}
