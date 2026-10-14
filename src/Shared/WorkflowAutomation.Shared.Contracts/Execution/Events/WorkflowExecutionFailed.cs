namespace WorkflowAutomation.Shared.Contracts.Execution.Events;

public record WorkflowExecutionFailed(
    Guid ExecutionId,
    Guid WorkspaceId,
    string FailedStepId,
    string Reason);
