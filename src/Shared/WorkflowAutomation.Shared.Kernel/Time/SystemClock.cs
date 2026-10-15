namespace WorkflowAutomation.Shared.Kernel.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
