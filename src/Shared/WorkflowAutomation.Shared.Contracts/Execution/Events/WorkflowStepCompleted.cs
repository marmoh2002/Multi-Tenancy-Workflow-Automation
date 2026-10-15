namespace WorkflowAutomation.Shared.Contracts.Execution.Events;

public record WorkflowStepCompleted(
    Guid ExecutionId,
    Guid WorkspaceId,
    string StepId,
    int AttemptNumber,
    string? OutputMetadata);
