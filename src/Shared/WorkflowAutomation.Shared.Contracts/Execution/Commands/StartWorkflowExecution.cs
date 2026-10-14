namespace WorkflowAutomation.Shared.Contracts.Execution.Commands;

public record StartWorkflowExecution(
    Guid ExecutionId,
    Guid WorkspaceId,
    Guid WorkflowId,
    Guid WorkflowVersionId,
    ExecutionSource Source,
    Guid? RequestedByUserId,
    Guid? ScheduleId,
    string? ScheduledOccurrenceId);
