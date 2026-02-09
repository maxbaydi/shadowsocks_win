using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Core.Orchestration;

namespace VibeShadowsocks.Platform.Tray;

public interface ITrayManager : IDisposable
{
    event EventHandler? ConnectRequested;

    event EventHandler? DisconnectRequested;

    event EventHandler? OpenRequested;

    event EventHandler? ExitRequested;

    event EventHandler<RoutingMode>? RoutingModeChanged;

    void Initialize(string appName, string? iconPath = null);

    void UpdateState(ConnectionState state, RoutingMode routingMode);

    void ShowNotification(string title, string message);
}
