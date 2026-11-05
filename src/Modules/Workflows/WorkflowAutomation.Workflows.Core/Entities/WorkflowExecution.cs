namespace WorkflowAutomation.Workflows.Core.Entities;

using System.Security.Cryptography;
using System.Text;

using WorkflowAutomation.Shared.Kernel.Primitives;
using WorkflowAutomation.Shared.Kernel.Time;
using WorkflowAutomation.Workflows.Core.Definitions;
using WorkflowAutomation.Workflows.Core.Enums;

public sealed class WorkflowExecution : AuditableEntity<Guid>, IMustHaveWorkspace
{
    // EF Core deserialization constructor
    private WorkflowExecution()
    {
    }
    private WorkflowExecution(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }
    public Guid WorkflowId { get; private set; }
    public Guid WorkflowVersionId { get; private set; }

    // Immutable snapshot of the workflow version at execution creation time
    public string WorkflowName { get; private set; } = string.Empty;
    public int VersionNumber { get; private set; }
    public int DefinitionSchemaVersion { get; private set; }
    public string DefinitionSnapshot { get; private set; } = string.Empty;
    public string DefinitionHash { get; private set; } = string.Empty;

    public WorkflowExecutionStatus Status { get; private set; }
    public WorkflowExecutionSource Source { get; private set; }

    public Guid? RequestedByUserId { get; private set; }
    public Guid? ScheduleId { get; private set; }
    public string? ScheduledOccurrenceId { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailedStepId { get; private set; }
    public string? FailureReason { get; private set; }

    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? FinishedAt => CompletedAt ?? CancelledAt;
    public bool IsCancellable { get; private set; } = true;


    public static WorkflowExecution Create(
        Guid id,
        Workflow workflow,
        WorkflowVersion version,
        WorkflowExecutionSource source,
        IClock clock,
        Guid? requestedByUserId = null,
        Guid? scheduleId = null,
        string? scheduledOccurrenceId = null)
    {
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source), source, "Invalid execution source.");

        if (requestedByUserId == Guid.Empty)
            throw new ArgumentException("RequestedByUserId cannot be Guid.Empty.", nameof(requestedByUserId));

        if (scheduleId == Guid.Empty)
            throw new ArgumentException("ScheduleId cannot be Guid.Empty.", nameof(scheduleId));

        if (source == WorkflowExecutionSource.Scheduled)
        {
            if (scheduleId is null)
                throw new ArgumentException("Scheduled executions must provide a ScheduleId.", nameof(scheduleId));

            if (string.IsNullOrWhiteSpace(scheduledOccurrenceId))
                throw new ArgumentException("Scheduled executions must provide a ScheduledOccurrenceId.", nameof(scheduledOccurrenceId));
            var trimmedOccurrence = scheduledOccurrenceId.Trim();
            if (trimmedOccurrence.Length > 200)
                throw new ArgumentException("ScheduledOccurrenceId cannot exceed 200 characters.", nameof(scheduledOccurrenceId));
            scheduledOccurrenceId = trimmedOccurrence;
        }
        else
        {
            if (scheduleId is not null || scheduledOccurrenceId is not null)
                throw new ArgumentException($"Executions with source '{source}' cannot have schedule identifiers.");
        }

        if (id == Guid.Empty)
            throw new ArgumentException("Execution ID cannot be empty.", nameof(id));

        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(clock);

        if (!version.IsPublished)
            throw new InvalidOperationException("Cannot create an execution for an unpublished workflow version.");

        if (version.WorkflowId != workflow.Id)
            throw new InvalidOperationException("The supplied version does not belong to the target workflow.");

        if (version.WorkspaceId != workflow.WorkspaceId)
            throw new InvalidOperationException("The supplied version does not belong to the same workspace as the workflow.");
        var normalizedOccurrenceId = scheduledOccurrenceId?.Trim();
        var execution = new WorkflowExecution(id)
        {
            WorkspaceId = workflow.WorkspaceId,
            WorkflowId = workflow.Id,
            WorkflowVersionId = version.Id,
            WorkflowName = workflow.Name,
            VersionNumber = version.VersionNumber,
            DefinitionSchemaVersion = version.DefinitionSchemaVersion,
            DefinitionSnapshot = version.SerializedDefinition,
            DefinitionHash = ComputeSha256Hash(version.SerializedDefinition),
            Status = WorkflowExecutionStatus.Pending,
            Source = source,
            RequestedByUserId = requestedByUserId,
            ScheduleId = scheduleId,
            ScheduledOccurrenceId = normalizedOccurrenceId
        };

        execution.MarkCreated(clock.UtcNow);
        return execution;
    }

    public void Start(IClock clock)
    {
        if (CancellationRequestedAt is not null)
            throw new InvalidOperationException(
                "Cannot start an execution with a pending cancellation request.");

        ArgumentNullException.ThrowIfNull(clock);

        if (Status != WorkflowExecutionStatus.Pending)
            throw new InvalidOperationException($"Cannot start an execution in '{Status}' status. Only Pending executions can be started.");

        var now = clock.UtcNow;
        Status = WorkflowExecutionStatus.Running;
        StartedAt = now;
        MarkUpdated(now);
    }

    public void Complete(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status != WorkflowExecutionStatus.Running)
            throw new InvalidOperationException($"Cannot complete an execution in '{Status}' status. Only Running executions can be completed.");

        Status = WorkflowExecutionStatus.Completed;
        CancellationRequestedAt = null;
        CancellationReason = null;
        var now = clock.UtcNow;
        CompletedAt = now;
        MarkUpdated(now);
    }

    public void Fail(string? failedStepId, string failureReason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("Failure reason cannot be null or whitespace.", nameof(failureReason));

        if (Status != WorkflowExecutionStatus.Pending && Status != WorkflowExecutionStatus.Running)
            throw new InvalidOperationException($"Cannot fail an execution in '{Status}' status. Only Pending or Running executions can be failed.");
        var normalizedStepId = string.IsNullOrWhiteSpace(failedStepId) ? null : failedStepId.Trim();
        if (normalizedStepId?.Length > 100)
            throw new ArgumentException("FailedStepId cannot exceed 100 characters.", nameof(failedStepId));
        var now = clock.UtcNow;
        Status = WorkflowExecutionStatus.Failed;
        FailedStepId = normalizedStepId;
        FailureReason = failureReason.Trim();
        CancellationRequestedAt = null;
        CancellationReason = null;
        CompletedAt = now;
        MarkUpdated(now);
    }
    public void PreventCancellation(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status != WorkflowExecutionStatus.Running)
            throw new InvalidOperationException($"Cannot prevent cancellation for an execution in '{Status}' status.");

        if (CancellationRequestedAt is not null)
            throw new InvalidOperationException("Cannot prevent cancellation after a cancellation request has already been recorded.");
        var now = clock.UtcNow;
        IsCancellable = false;
        MarkUpdated(now);


    }
    public void RequestCancellation(string reason, IClock clock)
    {
        // Idempotent: if already requested, preserve the initial request timestamp and reason
        if (Status == WorkflowExecutionStatus.Cancelled || CancellationRequestedAt is not null)
            return;
        // 2. Terminal check
        if (IsTerminal(Status))
            throw new InvalidOperationException($"Cannot request cancellation for an execution in terminal '{Status}' status.");
        // 3. Point of no return check
        if (!IsCancellable)
        {
            throw new InvalidOperationException(
                "This workflow has passed the point of no return and cannot be cancelled.");
        }
        // 4. Argument check
        ArgumentNullException.ThrowIfNull(clock);
        // 5. Reason validation
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation reason cannot be null or whitespace.", nameof(reason));
        var now = clock.UtcNow;
        CancellationRequestedAt = now;
        CancellationReason = reason.Trim();
        MarkUpdated(now);
    }

    public void MarkCancelled(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (CancellationRequestedAt is null)
            throw new InvalidOperationException("Cannot mark execution as cancelled without an existing cancellation request.");

        if (Status != WorkflowExecutionStatus.Pending && Status != WorkflowExecutionStatus.Running)
            throw new InvalidOperationException($"Cannot mark execution as cancelled from '{Status}' status.");

        var now = clock.UtcNow;
        Status = WorkflowExecutionStatus.Cancelled;
        CancelledAt = now;
        MarkUpdated(now);
    }

    public bool HasIntactSnapshot() =>
        ComputeSha256Hash(DefinitionSnapshot) == DefinitionHash;

    public WorkflowDefinition GetDefinition() =>
        WorkflowDefinitionSerializer.Deserialize(DefinitionSnapshot, DefinitionSchemaVersion);

    private static bool IsTerminal(WorkflowExecutionStatus status) =>
        status is WorkflowExecutionStatus.Completed
               or WorkflowExecutionStatus.Failed
               or WorkflowExecutionStatus.Cancelled;

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
