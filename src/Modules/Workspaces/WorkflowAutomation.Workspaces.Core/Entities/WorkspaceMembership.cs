namespace WorkflowAutomation.Workspaces.Core.Entities;

using WorkflowAutomation.Shared.Kernel.Primitives;
using WorkflowAutomation.Workspaces.Core.Enums;
public sealed class WorkspaceMembership : AuditableEntity<Guid>
{
    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public WorkspaceRole Role { get; private set; }
    public bool IsActive { get; private set; }
    // Required by EF Core
    private WorkspaceMembership()
    {
    }

    private WorkspaceMembership(Guid id, Guid workspaceId, Guid userId, WorkspaceRole role) : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
        IsActive = true;
    }
    public static WorkspaceMembership Create(Guid workspaceId, Guid userId, WorkspaceRole role) => Create(Guid.NewGuid(), workspaceId, userId, role);
    //overload to supply GUID
    public static WorkspaceMembership Create(Guid id, Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Membership ID cannot be empty.", nameof(id));
        }
        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException("Workspace ID cannot be empty.", nameof(workspaceId));
        }
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }
        return new WorkspaceMembership(id, workspaceId, userId, role);
    }

    public void UpdateRole(WorkspaceRole newRole)
    {
        Role = newRole;
    }
    public void Deactivate()
    {
        IsActive = false;
    }
    public void Activate()
    {
        IsActive = true;
    }

}