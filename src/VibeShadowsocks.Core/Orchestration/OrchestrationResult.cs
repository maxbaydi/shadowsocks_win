namespace VibeShadowsocks.Core.Orchestration;

public sealed record OrchestrationResult(bool Success, string Message, bool IsNoOp = false)
{
    public static OrchestrationResult Ok(string message = "OK") => new(true, message);

    public static OrchestrationResult Failed(string message) => new(false, message);

    public static OrchestrationResult NoOp(string message) => new(true, message, true);
}
