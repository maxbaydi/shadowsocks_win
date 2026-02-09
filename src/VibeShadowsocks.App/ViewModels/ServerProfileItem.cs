using CommunityToolkit.Mvvm.ComponentModel;
using VibeShadowsocks.Core.Models;

namespace VibeShadowsocks.App.ViewModels;

public partial class ServerProfileItem : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = "New Server";

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private int _port = 8388;

    [ObservableProperty]
    private string _method = "aes-256-gcm";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _group = string.Empty;

    [ObservableProperty]
    private string _tagsCsv = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string? _plugin;

    [ObservableProperty]
    private string? _pluginOptions;

    public string PasswordSecretId { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplaySummary => string.IsNullOrWhiteSpace(Host) ? Name : $"{Host}:{Port}";

    public ServerProfile ToDomainModel() => new()
    {
        Id = Id,
        Name = Name,
        Host = Host,
        Port = Port,
        Method = Method,
        Group = Group,
        Tags = TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        IsFavorite = IsFavorite,
        Plugin = string.IsNullOrWhiteSpace(Plugin) ? null : Plugin,
        PluginOptions = string.IsNullOrWhiteSpace(PluginOptions) ? null : PluginOptions,
        PasswordSecretId = PasswordSecretId,
    };

    public static ServerProfileItem FromDomainModel(ServerProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Host = profile.Host,
        Port = profile.Port,
        Method = profile.Method,
        Group = profile.Group,
        TagsCsv = string.Join(",", profile.Tags),
        IsFavorite = profile.IsFavorite,
        Plugin = profile.Plugin,
        PluginOptions = profile.PluginOptions,
        PasswordSecretId = profile.PasswordSecretId,
    };
}
