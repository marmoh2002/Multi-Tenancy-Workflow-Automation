namespace WorkflowAutomation.Shared.Kernel.Primitives;

/// <summary>
/// Marks an entity as tenant-scoped to a specific workspace.
/// Used by EF Core to automatically apply global query filters for multi-tenancy.
/// </summary>
public interface IMustHaveWorkspace
{
    Guid WorkspaceId { get; }
}
