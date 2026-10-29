namespace WorkflowAutomation.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WorkflowAutomation.Shared.Kernel.Context;

public sealed class ApplicationDbContextFactory
    : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "WORKFLOWAUTOMATION_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set the WORKFLOWAUTOMATION_CONNECTION_STRING environment " +
                "variable before running EF Core commands.");
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var workspaceContext = new WorkspaceContext();

        return new ApplicationDbContext(options, workspaceContext);
    }
}