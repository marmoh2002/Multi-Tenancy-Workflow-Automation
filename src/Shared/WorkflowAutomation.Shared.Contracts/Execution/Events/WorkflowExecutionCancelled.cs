namespace WorkflowAutomation.Shared.Contracts.Execution.Events;

public record WorkflowExecutionCancelled(
    Guid ExecutionId,
    Guid WorkspaceId,
    string Reason);
