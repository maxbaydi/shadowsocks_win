using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VibeShadowsocks.App.Helpers;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Importing;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.App.ViewModels;

public partial class ServersViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISecureStorage _secureStorage;

    [ObservableProperty]
    private ObservableCollection<ServerProfileItem> _profiles = [];

    [ObservableProperty]
    private ServerProfileItem? _selectedProfile;

    [ObservableProperty]
    private string _activeServerId = string.Empty;

    [ObservableProperty]
    private string _importText = string.Empty;

    [ObservableProperty]
    private string _exportText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public string TooltipSetActive => Loc.Get("TooltipSetActive");

    public ServersViewModel(ISettingsStore settingsStore, ISecureStorage secureStorage)
    {
        _settingsStore = settingsStore;
        _secureStorage = secureStorage;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await _settingsStore.LoadAsync();
            var items = new List<ServerProfileItem>();

            foreach (var profile in settings.ServerProfiles)
            {
                var item = ServerProfileItem.FromDomainModel(profile);
                item.Password = await _secureStorage.ReadSecretAsync(item.PasswordSecretId) ?? string.Empty;
                item.IsActive = profile.Id == settings.ActiveServerProfileId;
                items.Add(item);
            }

            Profiles = new ObservableCollection<ServerProfileItem>(items);
            ActiveServerId = settings.ActiveServerProfileId ?? string.Empty;
            SelectedProfile = Profiles.FirstOrDefault(p => p.IsActive) ?? Profiles.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Add()
    {
        var profile = new ServerProfileItem
        {
            Name = Loc.Get("NewServerName"),
            Port = 8388,
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SetActiveAsync()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = Loc.Get("SelectServerFirst");
            return;
        }

        foreach (var profile in Profiles)
        {
            profile.IsActive = profile.Id == SelectedProfile.Id;
        }

        ActiveServerId = SelectedProfile.Id;

        await _settingsStore.UpdateAsync(settings => settings with
        {
            ActiveServerProfileId = SelectedProfile.Id,
        });

        StatusMessage = Loc.Format("ActiveServerFmt", SelectedProfile.Name);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var profile in Profiles)
            {
                await _secureStorage.SaveSecretAsync(profile.PasswordSecretId, profile.Password);
            }

            var domainProfiles = Profiles.Select(profile => profile.ToDomainModel()).ToList();

            await _settingsStore.UpdateAsync(settings =>
            {
                var activeId = settings.ActiveServerProfileId;
                if (string.IsNullOrWhiteSpace(activeId) || domainProfiles.All(profile => profile.Id != activeId))
                {
                    activeId = domainProfiles.FirstOrDefault()?.Id;
                }

                return settings with
                {
                    ServerProfiles = domainProfiles,
                    ActiveServerProfileId = activeId,
                };
            });

            StatusMessage = Loc.Get("ProfilesSaved");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        IsBusy = true;
        try
        {
            var profile = SsUriParser.Parse(ImportText, out var password);
            var item = ServerProfileItem.FromDomainModel(profile);
            item.Password = password;

            Profiles.Add(item);
            SelectedProfile = item;
            ImportText = string.Empty;

            StatusMessage = Loc.Get("ImportedUri");
            await SaveAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = Loc.Format("ImportFailedFmt", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = Loc.Get("NoProfileSelected");
            return;
        }

        var password = await _secureStorage.ReadSecretAsync(SelectedProfile.PasswordSecretId) ?? SelectedProfile.Password;
        ExportText = SsUriParser.Export(SelectedProfile.ToDomainModel(), password);
        StatusMessage = Loc.Get("ExportComplete");
    }
}
