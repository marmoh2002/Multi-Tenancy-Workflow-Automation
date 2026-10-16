namespace WorkflowAutomation.Infrastructure.Persistence;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Workspaces.Core.Entities;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly IWorkspaceContext _workspaceContext;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IWorkspaceContext workspaceContext)
        : base(options)
    {
        _workspaceContext = workspaceContext;
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<WorkspaceConnection> WorkspaceConnections => Set<WorkspaceConnection>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // 1. Configures ASP.NET Core Identity tables (AspNetUsers, AspNetRoles, etc.)
        base.OnModelCreating(builder);

        // 2. Automatically applies all IEntityTypeConfiguration classes from this assembly
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // 3. Global Query Filters

        // builder.ApplyGlobalFilters<IMustHaveWorkspace>(e => e.WorkspaceId == (_workspaceContext.CurrentWorkspaceId ?? Guid.Empty));

        builder.Entity<Workspace>()
            .HasQueryFilter(w => w.Id == (_workspaceContext.CurrentWorkspaceId ?? Guid.Empty));

        builder.Entity<WorkspaceMembership>()
            .HasQueryFilter(m => m.WorkspaceId == (_workspaceContext.CurrentWorkspaceId ?? Guid.Empty));

        builder.Entity<WorkspaceConnection>()
            .HasQueryFilter(c => c.WorkspaceId == (_workspaceContext.CurrentWorkspaceId ?? Guid.Empty));
    }
}
