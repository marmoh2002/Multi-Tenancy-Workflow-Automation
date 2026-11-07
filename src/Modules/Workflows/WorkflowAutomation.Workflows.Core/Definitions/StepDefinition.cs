namespace WorkflowAutomation.Workflows.Core.Definitions;

public sealed class StepDefinition
{
    public const int MaxIdLength = 100;

    public required string Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public IReadOnlyList<string> DependsOn { get; init; } = [];
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    public static string NormalizeId(string stepId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        var normalized = stepId.Trim();
        if (normalized.Length > MaxIdLength)
        {
            throw new ArgumentException(
                $"Step ID cannot exceed {MaxIdLength} characters.",
                nameof(stepId));
        }

        return normalized;
    }
}