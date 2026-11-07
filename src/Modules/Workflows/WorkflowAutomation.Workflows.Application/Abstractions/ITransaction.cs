namespace WorkflowAutomation.Workflows.Application.Abstractions;

public interface ITransaction : IAsyncDisposable
{
    bool IsCompleted { get; }

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}