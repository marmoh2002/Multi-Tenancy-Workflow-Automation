namespace WorkflowAutomation.Infrastructure.Persistence;

using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Shared.Kernel.Primitives;
using WorkflowAutomation.Workflows.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Entities;

public partial class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly IWorkspaceContext _workspaceContext;
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IWorkspaceContext workspaceContext)
        : base(options)
    {
        _workspaceContext = workspaceContext;
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<WorkspaceConnection> WorkspaceConnections => Set<WorkspaceConnection>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // 1. Log only executed SQL commands to the Console at Information level
        optionsBuilder.LogTo(
            Console.WriteLine,
            new[] { DbLoggerCategory.Database.Command.Name }
            // ,LogLevel.Information
            , LogLevel.Error
            );

        // 2. Include parameter values in the logs (helpful to inspect query filter parameters)
        // optionsBuilder.EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // 1. Configures ASP.NET Core Identity tables (AspNetUsers, AspNetRoles, etc.)
        base.OnModelCreating(builder);
        // 2. Automatically applies all IEntityTypeConfiguration classes from this assembly
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Automatically apply workspace isolation filter to all IMustHaveWorkspace entities
        ApplyWorkspaceFilters(builder); // our extension method

    }
}
