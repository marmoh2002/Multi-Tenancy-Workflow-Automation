namespace DagWorkflow.Core.Definitions;

public sealed class WorkflowDefinition
{
    public IReadOnlyList<StepDefinition> Steps { get; init; } = [];
}