using System.Drawing;
using System.Windows.Forms;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Core.Orchestration;

namespace VibeShadowsocks.Platform.Tray;

public sealed class TrayManager : ITrayManager
{
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _menu = new();

    private readonly ToolStripMenuItem _connectItem = new("Connect");
    private readonly ToolStripMenuItem _disconnectItem = new("Disconnect");
    private readonly ToolStripMenuItem _routingItem = new("Routing Mode");
    private readonly ToolStripMenuItem _routingOffItem = new("Off") { CheckOnClick = true };
    private readonly ToolStripMenuItem _routingGlobalItem = new("Global") { CheckOnClick = true };
    private readonly ToolStripMenuItem _routingPacItem = new("PAC") { CheckOnClick = true };
    private readonly ToolStripMenuItem _openItem = new("Open");
    private readonly ToolStripMenuItem _exitItem = new("Exit");

    private bool _initialized;

    public event EventHandler? ConnectRequested;

    public event EventHandler? DisconnectRequested;

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler<RoutingMode>? RoutingModeChanged;

    public void Initialize(string appName, string? iconPath = null)
    {
        if (_initialized)
        {
            return;
        }

        _notifyIcon.Text = appName;
        _notifyIcon.Icon = ResolveIcon(iconPath);
        _notifyIcon.Visible = true;
        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        _connectItem.Click += (_, _) => ConnectRequested?.Invoke(this, EventArgs.Empty);
        _disconnectItem.Click += (_, _) => DisconnectRequested?.Invoke(this, EventArgs.Empty);
        _openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _routingOffItem.Click += (_, _) => OnRoutingMenuClicked(RoutingMode.Off);
        _routingGlobalItem.Click += (_, _) => OnRoutingMenuClicked(RoutingMode.Global);
        _routingPacItem.Click += (_, _) => OnRoutingMenuClicked(RoutingMode.Pac);

        _routingItem.DropDownItems.AddRange([_routingOffItem, _routingGlobalItem, _routingPacItem]);

        _menu.Items.AddRange([
            _connectItem,
            _disconnectItem,
            new ToolStripSeparator(),
            _routingItem,
            new ToolStripSeparator(),
            _openItem,
            _exitItem,
        ]);

        _initialized = true;
    }

    public void UpdateState(ConnectionState state, RoutingMode routingMode)
    {
        _connectItem.Enabled = state is ConnectionState.Disconnected or ConnectionState.Faulted;
        _disconnectItem.Enabled = state is ConnectionState.Connected or ConnectionState.Starting;

        _routingOffItem.Checked = routingMode == RoutingMode.Off;
        _routingGlobalItem.Checked = routingMode == RoutingMode.Global;
        _routingPacItem.Checked = routingMode == RoutingMode.Pac;

        _notifyIcon.Text = $"VibeShadowsocks ({state})";
    }

    public void ShowNotification(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.None;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private void OnRoutingMenuClicked(RoutingMode routingMode)
    {
        _routingOffItem.Checked = routingMode == RoutingMode.Off;
        _routingGlobalItem.Checked = routingMode == RoutingMode.Global;
        _routingPacItem.Checked = routingMode == RoutingMode.Pac;
        RoutingModeChanged?.Invoke(this, routingMode);
    }

    private static Icon ResolveIcon(string? iconPath)
    {
        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            try
            {
                return new Icon(iconPath);
            }
            catch
            {
                // fallback
            }
        }

        return SystemIcons.Shield;
    }
}
