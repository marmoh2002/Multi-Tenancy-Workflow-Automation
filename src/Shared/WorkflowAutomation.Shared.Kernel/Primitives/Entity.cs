namespace WorkflowAutomation.Shared.Kernel.Primitives;

public interface IEntity<TId>
    where TId : notnull
{
    TId Id { get; }
}

public abstract class Entity<TId> : IEntity<TId>
    where TId : notnull
{
    protected Entity(TId id)
    {
        Id = id;
    }

    protected Entity()
    {
        Id = default!;
    }

    public TId Id { get; private set; }
}