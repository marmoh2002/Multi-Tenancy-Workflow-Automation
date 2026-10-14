namespace WorkflowAutomation.Workflows.Core;

public sealed class DagValidationResult
{
    public bool IsValid { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public IReadOnlyList<string> OrderedStepIds { get; init; } = [];

    public static DagValidationResult Success(IReadOnlyList<string> orderedStepIds)
    {
        return new DagValidationResult
        {
            IsValid = true,
            OrderedStepIds = orderedStepIds
        };
    }

    public static DagValidationResult Failure(IReadOnlyList<string> errors)
    {
        return new DagValidationResult
        {
            IsValid = false,
            Errors = errors
        };
    }
}