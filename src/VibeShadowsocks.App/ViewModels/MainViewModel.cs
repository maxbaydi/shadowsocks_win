using CommunityToolkit.Mvvm.ComponentModel;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Orchestration;

namespace VibeShadowsocks.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConnectionOrchestrator _orchestrator;

    [ObservableProperty]
    private string _title = "VibeShadowsocks";

    [ObservableProperty]
    private string _connectionSummary = "Disconnected";

    public MainViewModel(IConnectionOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _orchestrator.StateChanged += (_, args) => ConnectionSummary = $"{args.Snapshot.State}: {args.Snapshot.Message}";
    }
}
