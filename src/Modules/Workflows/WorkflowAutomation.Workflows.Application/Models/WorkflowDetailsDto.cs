namespace WorkflowAutomation.Workflows.Application.Models;

public sealed record WorkflowDetailsDto
{
    public required Guid Id { get; init; }
    public required Guid WorkspaceId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required bool IsActive { get; init; }
    public Guid? ActiveVersionId { get; init; }
    public int? ActiveVersionNumber { get; init; }
    public DateTimeOffset? ActiveVersionPublishedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public uint RowVersion { get; init; }
}