namespace VibeShadowsocks.Core.Orchestration;

public sealed record OrchestratorCommand(OrchestratorCommandType Type, object? Payload = null)
{
    public static OrchestratorCommand Connect() => new(OrchestratorCommandType.Connect);

    public static OrchestratorCommand Disconnect() => new(OrchestratorCommandType.Disconnect);

    public static OrchestratorCommand Toggle() => new(OrchestratorCommandType.Toggle);

    public static OrchestratorCommand ApplyRouting() => new(OrchestratorCommandType.ApplyRouting);

    public static OrchestratorCommand UpdatePac() => new(OrchestratorCommandType.UpdatePac);

    public static OrchestratorCommand UpdateLists() => new(OrchestratorCommandType.UpdateLists);

    public static OrchestratorCommand RecoverProxy() => new(OrchestratorCommandType.RecoverProxy);
}
