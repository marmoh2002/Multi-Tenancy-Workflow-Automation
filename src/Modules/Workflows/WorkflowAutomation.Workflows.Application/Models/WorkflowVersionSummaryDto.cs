namespace WorkflowAutomation.Workflows.Application.Models;

public sealed record WorkflowVersionSummaryDto
{
    public required Guid Id { get; init; }
    public required Guid WorkflowId { get; init; }
    public required int VersionNumber { get; init; }
    public required int DefinitionSchemaVersion { get; init; }
    public required bool IsPublished { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public int ValidationErrorCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public uint RowVersion { get; init; }
}