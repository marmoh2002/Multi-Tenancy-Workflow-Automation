namespace WorkflowAutomation.Workflows.Core.Tests;

using WorkflowAutomation.Shared.Kernel.Time;

public sealed class TestClock : IClock
{
    public TestClock(DateTime? initialTime = null)
    {
        UtcNow = initialTime ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    public DateTime UtcNow { get; set; }

    public void Advance(TimeSpan duration)
    {
        UtcNow = UtcNow.Add(duration);
    }
}
