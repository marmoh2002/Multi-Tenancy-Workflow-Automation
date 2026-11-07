namespace WorkflowAutomation.Workflows.Core.Entities;

using WorkflowAutomation.Shared.Kernel.Primitives;
using WorkflowAutomation.Shared.Kernel.Time;
using WorkflowAutomation.Workflows.Core.Definitions;
using WorkflowAutomation.Workflows.Core.Enums;

public sealed class WorkflowStepExecution : AuditableEntity<Guid>, IMustHaveWorkspace
{
    public const int MaxStepIdLength = StepDefinition.MaxIdLength;
    // EF Core materialization constructor
    private WorkflowStepExecution()
    {
    }

    private WorkflowStepExecution(Guid id) : base(id)
    {
    }

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

    internal static WorkflowStepExecution CreatePending(
        Guid id,
        Guid workspaceId,
        Guid executionId,
        string stepId,
        int attemptNumber,
        IClock clock)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Step execution ID cannot be empty.",
                nameof(id));
        }

        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Workspace ID cannot be empty.",
                nameof(workspaceId));
        }

        if (executionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Execution ID cannot be empty.",
                nameof(executionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        var normalizedStepId = stepId.Trim();
        if (normalizedStepId.Length > MaxStepIdLength)
        {
            throw new ArgumentException(
                $"Step ID cannot exceed {MaxStepIdLength} characters.",
                nameof(stepId));
        }

        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptNumber),
                "Attempt number must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(clock);

        var stepExecution = new WorkflowStepExecution(id)
        {
            WorkspaceId = workspaceId,
            ExecutionId = executionId,
            StepId = normalizedStepId,
            AttemptNumber = attemptNumber,
            Status = WorkflowStepExecutionStatus.Pending,
            IsRetryable = false
        };

        stepExecution.MarkCreated(clock.UtcNow);
        return stepExecution;
    }

    public void Start(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status != WorkflowStepExecutionStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot start a step execution in '{Status}' status. " +
                "Only Pending step executions can be started.");
        }

        var now = clock.UtcNow;
        Status = WorkflowStepExecutionStatus.Running;
        StartedAt = now;
        MarkUpdated(now);
    }

    public void Complete(string? outputMetadata, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status != WorkflowStepExecutionStatus.Running)
        {
            throw new InvalidOperationException(
                $"Cannot complete a step execution in '{Status}' status. " +
                "Only Running step executions can be completed.");
        }

        var now = clock.UtcNow;
        Status = WorkflowStepExecutionStatus.Completed;
        OutputMetadata = outputMetadata;
        CompletedAt = now;
        MarkUpdated(now);
    }

    public void Fail(string errorMessage, bool isRetryable, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        if (Status != WorkflowStepExecutionStatus.Running)
        {
            throw new InvalidOperationException(
                $"Cannot fail a step execution in '{Status}' status. " +
                "Only Running step executions can be failed.");
        }

        var now = clock.UtcNow;
        Status = WorkflowStepExecutionStatus.Failed;
        ErrorMessage = errorMessage.Trim();
        IsRetryable = isRetryable;
        CompletedAt = now;
        MarkUpdated(now);
    }

    public void Cancel(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status != WorkflowStepExecutionStatus.Pending &&
            Status != WorkflowStepExecutionStatus.Running)
        {
            throw new InvalidOperationException(
                $"Cannot cancel a step execution in '{Status}' status. " +
                "Only Pending or Running step executions can be cancelled.");
        }

        var now = clock.UtcNow;
        Status = WorkflowStepExecutionStatus.Cancelled;
        CancelledAt = now;
        MarkUpdated(now);
    }

    public void Skip(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status != WorkflowStepExecutionStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot skip a step execution in '{Status}' status. " +
                "Only Pending step executions can be skipped.");
        }

        var now = clock.UtcNow;
        Status = WorkflowStepExecutionStatus.Skipped;
        CompletedAt = now;
        MarkUpdated(now);
    }
}
