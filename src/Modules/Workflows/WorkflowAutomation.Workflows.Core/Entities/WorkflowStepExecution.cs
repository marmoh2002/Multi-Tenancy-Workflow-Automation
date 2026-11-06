namespace WorkflowAutomation.Workflows.Core.Entities;

using WorkflowAutomation.Shared.Kernel.Primitives;
using WorkflowAutomation.Shared.Kernel.Time;
using WorkflowAutomation.Workflows.Core.Enums;

public sealed class WorkflowStepExecution : AuditableEntity<Guid>, IMustHaveWorkspace
{
    private WorkflowStepExecution() { }
    private WorkflowStepExecution(Guid id) : base(id) { }

    public Guid WorkspaceId { get; private set; }
    public Guid ExecutionId { get; private set; }

    public string StepId { get; private set; } = string.Empty;
    public int AttemptNumber { get; private set; }

    public WorkflowStepExecutionStatus Status { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? FinishedAt => CompletedAt ?? CancelledAt;

    public string? ErrorMessage { get; private set; }
    public bool IsRetryable { get; private set; }
    public string? OutputMetadata { get; private set; }

    public static WorkflowStepExecution CreatePending(
        Guid id,
        Guid workspaceId,
        Guid executionId,
        string stepId,
        int attemptNumber,
        IClock clock)
    {
        // Validation: IDs != Guid.Empty, attemptNumber > 0, stepId non-whitespace & <= 100 chars, clock != null
        // Sets Status = Pending, MarkCreated(clock.UtcNow)
    }

    public void Start(IClock clock) { ... }
    public void Complete(string? outputMetadata, IClock clock) { ... }
    public void Fail(string errorMessage, bool isRetryable, IClock clock) { ... }
    public void Cancel(IClock clock) { ... }
    public void Skip(IClock clock) { ... }
}