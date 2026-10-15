namespace WorkflowAutomation.Shared.Contracts.Execution.Commands;

public record ExecuteWorkflowStep(
    Guid ExecutionId,
    Guid WorkspaceId,
    Guid WorkflowId,
    Guid WorkflowVersionId,
    string StepId,
    int AttemptNumber);
