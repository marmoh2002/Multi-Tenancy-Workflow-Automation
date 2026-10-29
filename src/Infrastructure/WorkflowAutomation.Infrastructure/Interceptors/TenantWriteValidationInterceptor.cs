namespace WorkflowAutomation.Infrastructure.Interceptors;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Shared.Kernel.Primitives;

public sealed class TenantWriteValidationInterceptor : SaveChangesInterceptor
{
    private readonly IWorkspaceContext _workspaceContext;

    public TenantWriteValidationInterceptor(IWorkspaceContext workspaceContext)
    {
        _workspaceContext = workspaceContext;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        ValidateWorkspaceOwnership(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is null)
        {
            return base.SavingChanges(eventData, result);
        }

        ValidateWorkspaceOwnership(eventData.Context);
        return base.SavingChanges(eventData, result);
    }
    private void ValidateWorkspaceOwnership(DbContext context)
    {
        if (!_workspaceContext.HasCurrentWorkspace)
        {
            var hasTenantWrites = context.ChangeTracker.Entries<IMustHaveWorkspace>()
                .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

            if (hasTenantWrites)
            {
                throw new InvalidOperationException(
                    "Cannot mutate workspace-scoped entities without an active workspace context.");
            }

            return;
        }

        var currentWorkspaceId = _workspaceContext.CurrentWorkspaceId!.Value;

        foreach (var entry in context.ChangeTracker.Entries<IMustHaveWorkspace>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                if (entry.Entity.WorkspaceId != currentWorkspaceId)
                {
                    throw new InvalidOperationException(
                        $"Cross-workspace write detected: Cannot {entry.State} entity '{entry.Entity.GetType().Name}' " +
                        $"belonging to workspace '{entry.Entity.WorkspaceId}' while operating in workspace '{currentWorkspaceId}'.");
                }
            }
        }
    }

}
