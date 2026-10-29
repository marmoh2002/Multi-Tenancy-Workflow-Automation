using System.Text.Json;

namespace WorkflowAutomation.Workflows.Core.Definitions;

public static class WorkflowDefinitionSerializer
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static string Serialize(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        ValidateStructure(definition);

        return JsonSerializer.Serialize(definition, Options);
    }

    public static WorkflowDefinition Deserialize(
        string json,
        int schemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Definition schema version '{schemaVersion}' " +
                $"is not supported.");
        }

        ArgumentNullException.ThrowIfNull(json);

        // Inspect the JSON before deserialization.
        // Otherwise, a missing "steps" property would become
        // the default empty collection on WorkflowDefinition.
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                "The workflow definition must be a JSON object.");
        }

        if (!root.TryGetProperty("steps", out var steps))
        {
            throw new JsonException(
                "The workflow definition must contain a 'steps' property.");
        }

        if (steps.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "The 'steps' property must be a non-null array.");
        }

        var definition =
            root.Deserialize<WorkflowDefinition>(Options)
            ?? throw new JsonException(
                "The workflow definition cannot be null.");

        ValidateStructure(definition);

        return definition;
    }

    private static void ValidateStructure(
        WorkflowDefinition definition)
    {
        if (definition.Steps is null)
        {
            throw new JsonException(
                "The steps collection cannot be null.");
        }

        for (var index = 0; index < definition.Steps.Count; index++)
        {
            var step = definition.Steps[index];

            if (step is null)
            {
                throw new JsonException(
                        $"Step at index {index} cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(step.Id))
            {
                throw new JsonException(
                    $"Step at index {index} must have a non-empty Id.");
            }

            if (step.Type is null)
            {
                throw new JsonException(
                    $"Step '{step.Id}' cannot have a null Type.");
            }

            if (step.DependsOn is null)
            {
                throw new JsonException(
                    $"Step '{step.Id}' cannot have null dependencies.");
            }

            foreach (var dependency in step.DependsOn)
            {
                if (dependency is null)
                {
                    throw new JsonException(
                        $"Step '{step.Id}' cannot have a null " +
                        "dependency reference.");
                }
            }

            if (step.Parameters is null)
            {
                throw new JsonException(
                    $"Step '{step.Id}' cannot have null parameters.");
            }

            foreach (var parameter in step.Parameters)
            {
                if (parameter.Value is null)
                {
                    throw new JsonException(
                        $"Parameter '{parameter.Key}' on step " +
                        $"'{step.Id}' cannot have a null value.");
                }
            }
        }
    }
}