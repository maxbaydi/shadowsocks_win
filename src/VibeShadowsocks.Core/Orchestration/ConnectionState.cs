namespace VibeShadowsocks.Core.Orchestration;

public enum ConnectionState
{
    Disconnected = 0,
    Starting = 1,
    Connected = 2,
    Stopping = 3,
    Faulted = 4
}
