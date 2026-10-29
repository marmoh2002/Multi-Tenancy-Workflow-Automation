namespace WorkflowAutomation.Infrastructure.Persistence;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using WorkflowAutomation.Shared.Kernel.Primitives;
using WorkflowAutomation.Workspaces.Core.Entities;

public partial class ApplicationDbContext
{
    private static readonly MethodInfo ConfigureWorkspaceFilterMethod =
        typeof(ApplicationDbContext).GetMethod(
            nameof(ConfigureWorkspaceFilter),
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private void ApplyWorkspaceFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>()
            .HasQueryFilter(w => w.Id == _workspaceContext.CurrentWorkspaceId);

        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(IMustHaveWorkspace).IsAssignableFrom(t.ClrType) && t.BaseType is null)
            .ToList();

        foreach (var entityType in entityTypes)
        {
            ConfigureWorkspaceFilterMethod
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);
        }
    }

    private void ConfigureWorkspaceFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IMustHaveWorkspace
    {
        // Direct access to private field _workspaceContext declared in File 1
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => e.WorkspaceId == _workspaceContext.CurrentWorkspaceId);
    }
}
