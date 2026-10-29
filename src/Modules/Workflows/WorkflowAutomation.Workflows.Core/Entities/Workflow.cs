namespace WorkflowAutomation.Workflows.Core.Entities;

using WorkflowAutomation.Shared.Kernel.Primitives;
using WorkflowAutomation.Shared.Kernel.Time;

public sealed class Workflow : AuditableEntity<Guid>, IMustHaveWorkspace
{
    public Guid WorkspaceId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public bool IsActive { get; private set; }
    public Guid? ActiveVersionId { get; private set; }

    // Required by EF Core
    private Workflow()
    {
    }

    private Workflow(
        Guid id,
        Guid workspaceId,
        Guid createdByUserId,
        string name,
        string? description,
        IClock clock)
        : base(id)
    {
        WorkspaceId = workspaceId;
        CreatedByUserId = createdByUserId;
        Name = name;
        Description = description;
        IsActive = false;
        ActiveVersionId = null;

        MarkCreated(clock.UtcNow);
    }

    public static Workflow Create(
        Guid id,
        Guid workspaceId,
        Guid createdByUserId,
        string name,
        string? description,
        IClock clock)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Workflow ID cannot be empty.", nameof(id));
        }

        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException("Workspace ID cannot be empty.", nameof(workspaceId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Creator ID cannot be empty.", nameof(createdByUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedName = name.Trim();
        if (trimmedName.Length > 200)
        {
            throw new ArgumentException("Workflow name cannot exceed 200 characters.", nameof(name));
        }

        var trimmedDescription = description?.Trim();
        if (trimmedDescription?.Length > 2000)
        {
            throw new ArgumentException("Workflow description cannot exceed 2000 characters.", nameof(description));
        }

        return new Workflow(
            id,
            workspaceId,
            createdByUserId,
            trimmedName,
            trimmedDescription,
            clock);
    }

    public void UpdateDetails(
        string name,
        string? description,
        IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clock);

        var trimmedName = name.Trim();
        if (trimmedName.Length > 200)
        {
            throw new ArgumentException("Workflow name cannot exceed 200 characters.", nameof(name));
        }

        var trimmedDescription = description?.Trim();
        if (trimmedDescription?.Length > 2000)
        {
            throw new ArgumentException("Workflow description cannot exceed 2000 characters.", nameof(description));
        }

        if (Name == trimmedName && Description == trimmedDescription)
        {
            return;
        }

        Name = trimmedName;
        Description = trimmedDescription;
        MarkUpdated(clock.UtcNow);
    }

    public void Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (ActiveVersionId is null)
        {
            throw new InvalidOperationException("Cannot activate a workflow without a selected active version.");
        }

        if (IsActive)
        {
            return;
        }

        IsActive = true;
        MarkUpdated(clock.UtcNow);
    }

    public void Deactivate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        MarkUpdated(clock.UtcNow);
    }

    public void ActivateVersion(
        WorkflowVersion version,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(clock);

        if (version.WorkflowId != Id)
        {
            throw new InvalidOperationException("The version belongs to a different workflow.");
        }

        if (version.WorkspaceId != WorkspaceId)
        {
            throw new InvalidOperationException("The version belongs to a different workspace.");
        }

        if (!version.IsPublished)
        {
            throw new InvalidOperationException("Cannot activate an unpublished version.");
        }

        if (ActiveVersionId == version.Id)
        {
            return;
        }

        ActiveVersionId = version.Id;
        MarkUpdated(clock.UtcNow);
    }
}
