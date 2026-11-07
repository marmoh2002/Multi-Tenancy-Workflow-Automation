using WorkflowAutomation.Workflows.Application.Messaging;
using WorkflowAutomation.Workflows.Application.Models;

namespace WorkflowAutomation.Workflows.Application.Queries.ListWorkflows;

public sealed record ListWorkflowsQuery(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20)
    : IQuery<PagedResult<WorkflowSummaryDto>>;