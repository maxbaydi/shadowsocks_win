using Microsoft.Extensions.DependencyInjection;
using VibeShadowsocks.Platform.Hotkeys;
using VibeShadowsocks.Platform.SingleInstance;
using VibeShadowsocks.Platform.Startup;
using VibeShadowsocks.Platform.Tray;

namespace VibeShadowsocks.Platform.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVibeShadowsocksPlatform(this IServiceCollection services)
    {
        services.AddSingleton<ITrayManager, TrayManager>();
        services.AddSingleton<IHotkeyManager, HotkeyManager>();
        services.AddSingleton<ISingleInstanceManager, SingleInstanceManager>();
        services.AddSingleton<IStartupManager, StartupManager>();

        return services;
    }
}
