namespace WorkflowAutomation.Shared.Contracts.Execution.Events;

public record WorkflowStepCancelled(
    Guid ExecutionId,
    Guid WorkspaceId,
    string StepId,
    int AttemptNumber,
    string Reason);
