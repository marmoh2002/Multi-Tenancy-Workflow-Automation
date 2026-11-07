using WorkflowAutomation.Workflows.Core.Enums;

namespace WorkflowAutomation.Workflows.Application.Models;

public sealed record WorkflowExecutionSummaryDto
{
    public required Guid Id { get; init; }
    public required Guid WorkflowId { get; init; }
    public required Guid WorkflowVersionId { get; init; }
    public required string WorkflowName { get; init; }
    public required string WorkflowDisplayName { get; init; }
    public required int VersionNumber { get; init; }
    public required string VersionDisplayName { get; init; }
    public required WorkflowExecutionStatus Status { get; init; }
    public required WorkflowExecutionSource Source { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public string? FailedStepId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}