using WorkflowAutomation.Workflows.Core.Definitions;

namespace WorkflowAutomation.Workflows.Application.Models;

public sealed record WorkflowVersionDetailsDto
{
    public required Guid Id { get; init; }
    public required Guid WorkflowId { get; init; }
    public required int VersionNumber { get; init; }
    public required int DefinitionSchemaVersion { get; init; }
    public required bool IsPublished { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public required WorkflowDefinition Definition { get; init; }
    public required string SerializedDefinition { get; init; }
    public IReadOnlyList<string>? ValidationErrors { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public uint RowVersion { get; init; }
}