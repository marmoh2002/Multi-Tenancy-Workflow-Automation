namespace WorkflowAutomation.Workflows.Application.Abstractions;

public interface IWorkflowVersionNumberAllocator
{
    Task<int?> AllocateNextAsync(
        Guid workflowId,
        CancellationToken cancellationToken);
}