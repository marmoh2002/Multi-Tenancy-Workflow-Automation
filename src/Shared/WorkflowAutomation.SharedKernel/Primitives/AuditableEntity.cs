namespace WorkflowAutomation.SharedKernel.Primitives;

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
}
