namespace WorkflowAutomation.Shared.Contracts.Execution.Events;

public record WorkflowExecutionStarted(
    Guid ExecutionId,
    Guid WorkspaceId,
    Guid WorkflowId,
    Guid WorkflowVersionId);
