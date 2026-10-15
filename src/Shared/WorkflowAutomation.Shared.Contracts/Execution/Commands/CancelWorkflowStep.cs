namespace WorkflowAutomation.Shared.Contracts.Execution.Commands;

public record CancelWorkflowStep(
    Guid ExecutionId,
    Guid WorkspaceId,
    string StepId,
    string Reason);
