using WorkflowAutomation.Workflows.Core.Enums;

namespace WorkflowAutomation.Workflows.Application.Models;

public sealed record WorkflowExecutionDetailsDto
{
    public required Guid Id { get; init; }
    public required Guid WorkspaceId { get; init; }
    public required Guid WorkflowId { get; init; }
    public required Guid WorkflowVersionId { get; init; }
    public required string WorkflowName { get; init; }
    public required string WorkflowDisplayName { get; init; }
    public required int VersionNumber { get; init; }
    public required string VersionDisplayName { get; init; }
    public required int DefinitionSchemaVersion { get; init; }
    public required string DefinitionSnapshot { get; init; }
    public required string DefinitionHash { get; init; }
    public required bool IsSnapshotIntact { get; init; }
    public required WorkflowExecutionStatus Status { get; init; }
    public required WorkflowExecutionSource Source { get; init; }
    public Guid? RequestedByUserId { get; init; }
    public Guid? ScheduleId { get; init; }
    public string? ScheduledOccurrenceId { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? FailedStepId { get; init; }
    public string? FailureReason { get; init; }
    public DateTimeOffset? CancellationRequestedAt { get; init; }
    public string? CancellationReason { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}