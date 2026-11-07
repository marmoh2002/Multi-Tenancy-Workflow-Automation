namespace WorkflowAutomation.Shared.Kernel.Primitives;

public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; }

    DateTimeOffset? UpdatedAt { get; }
}

public abstract class AuditableEntity<TId>
    : Entity<TId>, IAuditableEntity
    where TId : notnull
{
    protected AuditableEntity(TId id)
        : base(id)
    {
    }

    protected AuditableEntity()
    {
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    // Currently, CreatedAt and UpdatedAt have private set.
    // Derived domain entities (which we will build later 
    // for Workflow and WorkflowVersion) cannot
    // set these values during creation or state 
    // transitions without protected mutators.

    // Adding these methods allows derived entities 
    // to control their lifecycle timestamps
    // while keeping the setters inaccessible to outside callers.

    protected void MarkCreated(DateTimeOffset createdAt)
    {
        CreatedAt = createdAt;
    }

    protected void MarkUpdated(DateTimeOffset updatedAt)
    {
        UpdatedAt = updatedAt;
    }


}
