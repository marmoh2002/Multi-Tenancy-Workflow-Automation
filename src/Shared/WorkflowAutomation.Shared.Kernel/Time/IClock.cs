namespace WorkflowAutomation.Shared.Kernel.Time;

public interface IClock
{
    DateTime UtcNow { get; }
}
