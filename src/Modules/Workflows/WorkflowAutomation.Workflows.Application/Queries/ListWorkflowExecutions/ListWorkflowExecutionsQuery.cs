using WorkflowAutomation.Workflows.Application.Messaging;
using WorkflowAutomation.Workflows.Application.Models;
using WorkflowAutomation.Workflows.Core.Enums;

namespace WorkflowAutomation.Workflows.Application.Queries.ListWorkflowExecutions;

public sealed record ListWorkflowExecutionsQuery(
    Guid? WorkflowId = null,
    WorkflowExecutionStatus? Status = null,
    int Page = 1,
    int PageSize = 20)
    : IQuery<PagedResult<WorkflowExecutionSummaryDto>>;