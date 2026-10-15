namespace WorkflowAutomation.Shared.Kernel.Context;

/// <summary>
/// Provides access to the resolved workspace and user context for the current scope
/// (HTTP request, queue message consumer, or background job).
/// </summary>
public interface IWorkspaceContext
{
    Guid? CurrentWorkspaceId { get; }

    Guid? CurrentUserId { get; }

    bool HasCurrentWorkspace => CurrentWorkspaceId.HasValue && CurrentWorkspaceId.Value != Guid.Empty;
}
