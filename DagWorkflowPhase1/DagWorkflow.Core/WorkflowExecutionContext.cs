namespace DagWorkflow.Core;

public sealed class WorkflowExecutionContext
{
    public List<string> Logs { get; } = [];

    public Dictionary<string, bool> ConditionResults { get; } = [];
}