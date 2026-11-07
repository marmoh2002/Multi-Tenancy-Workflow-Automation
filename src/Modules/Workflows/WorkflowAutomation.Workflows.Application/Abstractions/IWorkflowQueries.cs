using WorkflowAutomation.Workflows.Application.Models;
using WorkflowAutomation.Workflows.Application.Queries.ListWorkflowExecutions;
using WorkflowAutomation.Workflows.Application.Queries.ListWorkflows;

namespace WorkflowAutomation.Workflows.Application.Abstractions;

public interface IWorkflowQueries
{
    Task<WorkflowDetailsDto?> GetWorkflowAsync(
        Guid workflowId,
        CancellationToken cancellationToken);

    Task<PagedResult<WorkflowSummaryDto>> ListWorkflowsAsync(
        ListWorkflowsQuery query,
        CancellationToken cancellationToken);

    Task<WorkflowVersionDetailsDto?> GetVersionAsync(
        Guid workflowId,
        Guid versionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowVersionSummaryDto>> ListVersionsAsync(
        Guid workflowId,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionDetailsDto?> GetExecutionAsync(
        Guid executionId,
        CancellationToken cancellationToken);

    Task<PagedResult<WorkflowExecutionSummaryDto>> ListExecutionsAsync(
        ListWorkflowExecutionsQuery query,
        CancellationToken cancellationToken);
}