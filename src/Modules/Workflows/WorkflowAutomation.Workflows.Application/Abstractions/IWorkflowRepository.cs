using WorkflowAutomation.Workflows.Core.Entities;

namespace WorkflowAutomation.Workflows.Application.Abstractions;

public interface IWorkflowRepository
{
    Task<Workflow?> GetByIdAsync(
        Guid workflowId,
        CancellationToken cancellationToken);

    Task<WorkflowVersion?> GetVersionAsync(
        Guid workflowId,
        Guid versionId,
        CancellationToken cancellationToken);

    Task AddAsync(
        Workflow workflow,
        CancellationToken cancellationToken);

    Task AddVersionAsync(
        WorkflowVersion version,
        CancellationToken cancellationToken);

    void Remove(Workflow workflow);

    void RemoveVersion(WorkflowVersion version);

    Task<bool> HasNonTerminalExecutionsAsync(
        Guid workflowId,
        CancellationToken cancellationToken);

    Task<bool> LockAsync(
        Guid workflowId,
        CancellationToken cancellationToken);
}