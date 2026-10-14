namespace WorkflowAutomation.Shared.Contracts.Execution.Commands;

public record CancelWorkflowExecution(
    Guid ExecutionId,
    Guid WorkspaceId,
    string Reason);
