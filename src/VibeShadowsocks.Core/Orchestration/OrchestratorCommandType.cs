namespace VibeShadowsocks.Core.Orchestration;

public enum OrchestratorCommandType
{
    Connect = 0,
    Disconnect = 1,
    Toggle = 2,
    ApplyRouting = 3,
    UpdatePac = 4,
    UpdateLists = 5,
    RecoverProxy = 6,
    FaultFromProcessExit = 7
}
