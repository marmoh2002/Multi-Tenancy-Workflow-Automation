namespace WorkflowAutomation.Workflows.Core.Definitions;

public sealed class StepDefinition
{
    public required string Id { get; init; }

    public string Type { get; init; } = string.Empty;

    public IReadOnlyList<string> DependsOn { get; init; } = [];

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}