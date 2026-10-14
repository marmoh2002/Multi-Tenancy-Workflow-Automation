namespace WorkflowAutomation.Shared.Contracts.Execution.Events;

public record WorkflowExecutionCompleted(
    Guid ExecutionId,
    Guid WorkspaceId);
