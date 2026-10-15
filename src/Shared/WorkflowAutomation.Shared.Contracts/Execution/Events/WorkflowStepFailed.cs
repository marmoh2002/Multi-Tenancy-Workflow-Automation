namespace WorkflowAutomation.Shared.Contracts.Execution.Events;

public record WorkflowStepFailed(
    Guid ExecutionId,
    Guid WorkspaceId,
    string StepId,
    int AttemptNumber,
    string ErrorMessage,
    bool IsRetryable);
