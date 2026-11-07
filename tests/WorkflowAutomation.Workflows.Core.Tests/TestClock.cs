namespace WorkflowAutomation.Workflows.Core.Tests;

using System;
using WorkflowAutomation.Shared.Kernel.Time;

public sealed class TestClock : IClock
{
    private DateTime _utcNow;
    private readonly TimeSpan? _advancePerRead;

    public TestClock(DateTime? initialTime = null, TimeSpan? advancePerRead = null)
    {
        _utcNow = initialTime ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _advancePerRead = advancePerRead;
    }
    /// <summary>
    /// Tracks total reads of UtcNow, including assertions. Capture this count immediately before a transition to measure operation reads.
    /// </summary>
    public int ReadsCount { get; private set; }

    public DateTime UtcNow
    {
        get
        {
            ReadsCount++;
            var current = _utcNow;
            if (_advancePerRead.HasValue)
            {
                _utcNow = _utcNow.Add(_advancePerRead.Value);
            }
            return current;
        }
        set => _utcNow = value;
    }

    public void Advance(TimeSpan duration)
    {
        _utcNow = _utcNow.Add(duration);
    }
}