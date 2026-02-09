namespace VibeShadowsocks.Core.Orchestration;

public sealed class ConnectionStateChangedEventArgs(ConnectionStatusSnapshot snapshot) : EventArgs
{
    public ConnectionStatusSnapshot Snapshot { get; } = snapshot;
}
