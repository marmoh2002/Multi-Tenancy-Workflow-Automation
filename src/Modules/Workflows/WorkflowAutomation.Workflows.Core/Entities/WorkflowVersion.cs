namespace WorkflowAutomation.Workflows.Core.Entities;

using System.Text.Encodings.Web;
using System.Text.Json;
using WorkflowAutomation.Shared.Kernel.Primitives;
using WorkflowAutomation.Shared.Kernel.Time;
using WorkflowAutomation.Workflows.Core.Definitions;

public sealed class WorkflowVersion : AuditableEntity<Guid>, IMustHaveWorkspace
{
    public const int CurrentDefinitionSchemaVersion =
    WorkflowDefinitionSerializer.CurrentSchemaVersion;

    public Guid WorkspaceId { get; private set; }
    public Guid WorkflowId { get; private set; }

    public int VersionNumber { get; private set; }
    public int DefinitionSchemaVersion { get; private set; }

    public bool IsPublished { get; private set; }

    public string SerializedDefinition { get; private set; } = string.Empty;

    public string? ValidationErrorsJson { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    // Required by EF Core
    private WorkflowVersion()
    {
    }

    private WorkflowVersion(
        Guid id,
        Guid workspaceId,
        Guid workflowId,
        int versionNumber,
        int definitionSchemaVersion,
        string serializedDefinition,
        string? validationErrorsJson,
        IClock clock)
        : base(id)
    {
        WorkspaceId = workspaceId;
        WorkflowId = workflowId;
        VersionNumber = versionNumber;
        DefinitionSchemaVersion = definitionSchemaVersion;
        SerializedDefinition = serializedDefinition;
        ValidationErrorsJson = validationErrorsJson;
        IsPublished = false;
        PublishedAt = null;

        MarkCreated(clock.UtcNow);
    }

    public static WorkflowVersion CreateDraft(
        Guid id,
        Guid workspaceId,
        Guid workflowId,
        int versionNumber,
        WorkflowDefinition definition,
        IDagValidator validator,
        IClock clock)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Version ID cannot be empty.", nameof(id));
        }

        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException("Workspace ID cannot be empty.", nameof(workspaceId));
        }

        if (workflowId == Guid.Empty)
        {
            throw new ArgumentException("Workflow ID cannot be empty.", nameof(workflowId));
        }

        if (versionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(versionNumber),
                "Version number must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(clock);

        // 1. Serialize in-memory definition (applies structural validation)
        var serialized = WorkflowDefinitionSerializer.Serialize(definition);

        // 2. Deserialize snapshot to verify round trip and obtain isolated instance
        var snapshot = WorkflowDefinitionSerializer.Deserialize(
            serialized,
            CurrentDefinitionSchemaVersion);

        // 3. Validate snapshot with the DAG validator
        var validationResult = validator.Validate(snapshot);

        // 4. Store validation errors as JSON only when invalid; keep SQL NULL when valid
        string? validationErrorsJson = null;
        if (!validationResult.IsValid)
        {
            validationErrorsJson = JsonSerializer.Serialize(validationResult.Errors, Options);
        }

        return new WorkflowVersion(
            id,
            workspaceId,
            workflowId,
            versionNumber,
            CurrentDefinitionSchemaVersion,
            serialized,
            validationErrorsJson,
            clock);
    }

    public WorkflowDefinition GetDefinition()
    {
        return WorkflowDefinitionSerializer.Deserialize(
            SerializedDefinition,
            DefinitionSchemaVersion);
    }

    public void Publish(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (IsPublished)
        {
            throw new InvalidOperationException(
                "Workflow version is already published.");
        }

        if (ValidationErrorsJson is not null)
        {
            throw new InvalidOperationException(
                "Cannot publish a workflow version with validation errors.");
        }

        var definition = GetDefinition();
        if (definition.Steps.Count == 0)
        {
            throw new InvalidOperationException(
                "Cannot publish an empty workflow definition.");
        }

        var now = clock.UtcNow;
        IsPublished = true;
        PublishedAt = now;
        MarkUpdated(now);
    }
}
