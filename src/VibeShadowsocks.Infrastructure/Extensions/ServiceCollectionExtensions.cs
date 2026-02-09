using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Infrastructure.Diagnostics;
using VibeShadowsocks.Infrastructure.Networking;
using VibeShadowsocks.Infrastructure.Options;
using VibeShadowsocks.Infrastructure.Pac;
using VibeShadowsocks.Infrastructure.Processes;
using VibeShadowsocks.Infrastructure.Proxy;
using VibeShadowsocks.Infrastructure.Storage;

namespace VibeShadowsocks.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVibeShadowsocksInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(_ =>
        {
            var paths = AppPaths.CreateDefault();
            Directory.CreateDirectory(paths.RootDirectory);
            Directory.CreateDirectory(paths.LogsDirectory);
            Directory.CreateDirectory(paths.RuntimeDirectory);
            Directory.CreateDirectory(paths.CacheDirectory);
            Directory.CreateDirectory(paths.StateDirectory);
            Directory.CreateDirectory(paths.SecretsDirectory);
            return paths;
        });

        services.AddHttpClient(nameof(HttpCache), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeShadowsocks/1.0");
        });

        services.AddSingleton<HttpCache>();

        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<ISecureStorage, SecureStorage>();
        services.AddSingleton<ISsLocalRunner, SsLocalRunner>();
        services.AddSingleton<ISystemProxyManager, SystemProxyManager>();

        services.AddSingleton<LocalPacHttpServer>();
        services.AddSingleton<GitHubPresetProvider>();
        services.AddSingleton<IPacManager, PacManager>();

        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();

        services.AddSingleton<SimpleFileLoggerProvider>();
        services.AddSingleton<ILoggerProvider>(serviceProvider => serviceProvider.GetRequiredService<SimpleFileLoggerProvider>());

        return services;
    }
}
