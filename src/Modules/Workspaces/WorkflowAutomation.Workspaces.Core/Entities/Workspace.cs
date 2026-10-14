namespace WorkflowAutomation.Workspaces.Core.Entities;

using WorkflowAutomation.Shared.Kernel.Primitives;
using WorkflowAutomation.Workspaces.Core.Enums;

public class Workspace : AuditableEntity<Guid>
{
    // Required by EF Core
    private Workspace()
    {
    }
    
    private Workspace(
        Guid id,
        string name,
        WorkspaceType type,
        Guid createdByUserId,
        string? description = null)
        : base(id)
    {
        Name = name;
        Type = type;
        Description = description;
        CreatedByUserId = createdByUserId;
        Status = WorkspaceStatus.Active;
    }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; } = string.Empty;
    public WorkspaceType Type { get; private set; }
    public WorkspaceStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public static Workspace CreatePersonal(Guid id, Guid createdByUserId, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Workspace ID cannot be empty.",
                nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Creator ID cannot be empty.",
                nameof(createdByUserId));
        }

        return new Workspace(
            id,
            name.Trim(),
            WorkspaceType.Personal,
            createdByUserId);
    }

    public static Workspace CreateOrganization(
        Guid id,
        Guid createdByUserId,
        string name,
        string? description = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Workspace ID cannot be empty.",
                nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Creator ID cannot be empty.",
                nameof(createdByUserId));
        }

        return new Workspace(
            id,
            name.Trim(),
            WorkspaceType.Organization,
            createdByUserId,
            description?.Trim());
    }
    public void UpdateDetails(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
    }
    public void Suspend()
    {
        Status = WorkspaceStatus.Suspended;
    }
    public void Activate()
    {
        Status = WorkspaceStatus.Active;
    }
    public void Archive()
    {
        Status = WorkspaceStatus.Archived;
    }
}
