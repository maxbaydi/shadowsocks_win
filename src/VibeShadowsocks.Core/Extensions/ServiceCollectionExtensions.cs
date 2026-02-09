using Microsoft.Extensions.DependencyInjection;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Orchestration;
using VibeShadowsocks.Core.Validation;

namespace VibeShadowsocks.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVibeShadowsocksCore(
        this IServiceCollection services,
        Action<ConnectionOrchestratorOptions>? configure = null)
    {
        var options = new ConnectionOrchestratorOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ISettingsValidator, SettingsValidator>();
        services.AddSingleton<IPortAvailabilityProbe, SocketPortAvailabilityProbe>();
        services.AddSingleton<IConnectionOrchestrator, ConnectionOrchestrator>();

        return services;
    }
}
