using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VibeShadowsocks.App.Helpers;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.App.ViewModels;

public partial class RoutingViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IConnectionOrchestrator _orchestrator;
    private readonly IPacManager _pacManager;
    private bool _suppressHandlers;

    [ObservableProperty]
    private ObservableCollection<PacProfile> _pacProfiles = [];

    [ObservableProperty]
    private PacProfile? _selectedPacProfile;

    [ObservableProperty]
    private bool _isRemotePac;

    [ObservableProperty]
    private string _remotePacUrl = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _proxyDomains = [];

    [ObservableProperty]
    private ObservableCollection<string> _directDomains = [];

    [ObservableProperty]
    private string _newProxyDomain = string.Empty;

    [ObservableProperty]
    private string _newDirectDomain = string.Empty;

    [ObservableProperty]
    private bool _defaultActionIsProxy = true;

    [ObservableProperty]
    private bool _bypassPrivateAddresses = true;

    [ObservableProperty]
    private bool _bypassSimpleHostnames = true;

    [ObservableProperty]
    private string _rulesUrl = string.Empty;

    [ObservableProperty]
    private string _pacPreview = string.Empty;

    [ObservableProperty]
    private string _testInput = "https://google.com";

    [ObservableProperty]
    private string _testOutput = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAndApplyCommand))]
    private bool _hasUnsavedChanges;

    public string TooltipAddDomain => Loc.Get("TooltipAddDomain");

    public Visibility ManagedVisibility => IsRemotePac ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RemoteVisibility => IsRemotePac ? Visibility.Visible : Visibility.Collapsed;

    public RoutingViewModel(
        ISettingsStore settingsStore,
        IConnectionOrchestrator orchestrator,
        IPacManager pacManager)
    {
        _settingsStore = settingsStore;
        _orchestrator = orchestrator;
        _pacManager = pacManager;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        _suppressHandlers = true;
        try
        {
            var settings = await _settingsStore.LoadAsync();
            PacProfiles = new ObservableCollection<PacProfile>(settings.PacProfiles);
            SelectedPacProfile = settings.GetActivePacProfile() ?? PacProfiles.FirstOrDefault();

            if (SelectedPacProfile is not null)
            {
                LoadProfileData(SelectedPacProfile);
            }

            PacPreview = _pacManager.GetPacPreview();
            HasUnsavedChanges = false;
        }
        finally
        {
            _suppressHandlers = false;
            IsBusy = false;
        }
    }

    partial void OnSelectedPacProfileChanged(PacProfile? value)
    {
        if (_suppressHandlers || value is null)
        {
            return;
        }

        _suppressHandlers = true;
        LoadProfileData(value);
        HasUnsavedChanges = false;
        _suppressHandlers = false;
    }

    partial void OnIsRemotePacChanged(bool value)
    {
        OnPropertyChanged(nameof(ManagedVisibility));
        OnPropertyChanged(nameof(RemoteVisibility));
        MarkDirty();
    }

    partial void OnRemotePacUrlChanged(string value) => MarkDirty();
    partial void OnDefaultActionIsProxyChanged(bool value) => MarkDirty();
    partial void OnBypassPrivateAddressesChanged(bool value) => MarkDirty();
    partial void OnBypassSimpleHostnamesChanged(bool value) => MarkDirty();
    partial void OnRulesUrlChanged(string value) => MarkDirty();

    [RelayCommand]
    private void AddProxyDomain()
    {
        var domain = NormalizeDomain(NewProxyDomain);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        if (ProxyDomains.Any(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var existing = DirectDomains.FirstOrDefault(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            DirectDomains.Remove(existing);
        }

        ProxyDomains.Add(domain);
        NewProxyDomain = string.Empty;
        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private void RemoveProxyDomain(string domain)
    {
        ProxyDomains.Remove(domain);
        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private void AddDirectDomain()
    {
        var domain = NormalizeDomain(NewDirectDomain);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        if (DirectDomains.Any(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var existing = ProxyDomains.FirstOrDefault(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ProxyDomains.Remove(existing);
        }

        DirectDomains.Add(domain);
        NewDirectDomain = string.Empty;
        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private void RemoveDirectDomain(string domain)
    {
        DirectDomains.Remove(domain);
        HasUnsavedChanges = true;
    }

    private bool CanSaveAndApply() => HasUnsavedChanges;

    [RelayCommand(CanExecute = nameof(CanSaveAndApply))]
    private async Task SaveAndApplyAsync()
    {
        if (SelectedPacProfile is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updatedProfile = IsRemotePac
                ? SelectedPacProfile with
                {
                    Type = PacProfileType.Remote,
                    RemotePacUrl = RemotePacUrl.Trim(),
                }
                : SelectedPacProfile with
                {
                    Type = PacProfileType.Managed,
                    RemotePacUrl = null,
                    InlineRules = BuildInlineRules(),
                    DefaultAction = DefaultActionIsProxy ? PacDefaultAction.Proxy : PacDefaultAction.Direct,
                    BypassPrivateAddresses = BypassPrivateAddresses,
                    BypassSimpleHostnames = BypassSimpleHostnames,
                    RulesUrl = string.IsNullOrWhiteSpace(RulesUrl) ? null : RulesUrl.Trim(),
                };

            await _settingsStore.UpdateAsync(settings =>
            {
                var updatedProfiles = settings.PacProfiles
                    .Select(p => p.Id == updatedProfile.Id ? updatedProfile : p)
                    .ToList();
                return settings with { PacProfiles = updatedProfiles, ActivePacProfileId = updatedProfile.Id };
            });

            var currentSettings = await _settingsStore.LoadAsync();
            if (currentSettings.RoutingMode == RoutingMode.Pac)
            {
                if (!IsRemotePac)
                {
                    await _pacManager.UpdateManagedPacAsync(updatedProfile, currentSettings.Ports.SocksPort);
                }

                await _orchestrator.ApplyRoutingAsync();
            }

            PacPreview = IsRemotePac ? Loc.Format("RemotePacFmt", RemotePacUrl) : _pacManager.GetPacPreview();

            _suppressHandlers = true;
            SelectedPacProfile = updatedProfile;
            _suppressHandlers = false;

            HasUnsavedChanges = false;
            StatusMessage = IsRemotePac
                ? Loc.Get("RemotePacSaved")
                : Loc.Get("RulesSaved");
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("ErrorFmt", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (SelectedPacProfile is null)
        {
            TestOutput = Loc.Get("NoProfileSelected");
            return;
        }

        if (IsRemotePac)
        {
            TestOutput = Loc.Get("TestRemoteNA");
            return;
        }

        var settings = await _settingsStore.LoadAsync();
        var result = _pacManager.TestRule(SelectedPacProfile, TestInput, settings.Ports.SocksPort);
        TestOutput = $"{result.Decision} ({result.Reason})";
    }

    private void LoadProfileData(PacProfile profile)
    {
        IsRemotePac = profile.Type == PacProfileType.Remote;
        RemotePacUrl = profile.RemotePacUrl ?? string.Empty;

        ProxyDomains.Clear();
        DirectDomains.Clear();

        foreach (var line in (profile.InlineRules ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('#') || line.StartsWith('!') || line.StartsWith("//"))
            {
                continue;
            }

            if (line.StartsWith("@@||"))
            {
                var domain = line[4..].TrimEnd('/').Trim();
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    DirectDomains.Add(domain);
                }
            }
            else if (line.StartsWith("||"))
            {
                var domain = line[2..].TrimEnd('/').Trim();
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    ProxyDomains.Add(domain);
                }
            }
        }

        DefaultActionIsProxy = profile.DefaultAction == PacDefaultAction.Proxy;
        BypassPrivateAddresses = profile.BypassPrivateAddresses;
        BypassSimpleHostnames = profile.BypassSimpleHostnames;
        RulesUrl = profile.RulesUrl ?? string.Empty;
    }

    private string BuildInlineRules()
    {
        var lines = new List<string>(ProxyDomains.Count + DirectDomains.Count);
        foreach (var domain in ProxyDomains)
        {
            lines.Add($"||{domain}");
        }

        foreach (var domain in DirectDomains)
        {
            lines.Add($"@@||{domain}");
        }

        return string.Join('\n', lines);
    }

    private void MarkDirty()
    {
        if (!_suppressHandlers)
        {
            HasUnsavedChanges = true;
        }
    }

    private static string NormalizeDomain(string input)
    {
        var domain = input.Trim().ToLowerInvariant();

        if (Uri.TryCreate(domain, UriKind.Absolute, out var uri))
        {
            domain = uri.Host;
        }

        domain = domain.TrimStart('.').TrimEnd('/');

        if (domain.StartsWith("www."))
        {
            domain = domain[4..];
        }

        return domain;
    }
}
