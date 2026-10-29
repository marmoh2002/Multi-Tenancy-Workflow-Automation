namespace WorkflowAutomation.Shared.Kernel.Context;

public class WorkspaceContext : IWorkspaceContext
{
    private readonly object _lock = new();
    private bool _isInitialized;

    public Guid? CurrentWorkspaceId { get; private set; }
    public Guid? CurrentUserId { get; private set; }

    public bool HasCurrentWorkspace =>
        CurrentWorkspaceId.HasValue && CurrentWorkspaceId.Value != Guid.Empty;

    /// <summary>
    /// Initializes the workspace context for the current operation scope.
    /// Can only be called once per scope to prevent accidental tampering.
    /// </summary>
    public void SetContext(Guid workspaceId, Guid? userId = null)
    {
        lock (_lock)
        {
            if (_isInitialized)
            {
                throw new InvalidOperationException("Workspace context has already been initialized for the current scope.");
            }

            if (workspaceId == Guid.Empty)
            {
                throw new ArgumentException("Workspace ID cannot be empty.", nameof(workspaceId));
            }

            CurrentWorkspaceId = workspaceId;
            CurrentUserId = userId;
            _isInitialized = true;
        }
    }
}