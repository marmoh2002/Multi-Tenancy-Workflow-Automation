namespace WorkflowAutomation.SharedKernel.Time;

public interface IClock
{
    DateTime UtcNow { get; }
}
