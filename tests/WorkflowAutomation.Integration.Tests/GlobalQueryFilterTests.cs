namespace WorkflowAutomation.Integration.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Infrastructure.Persistence;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Workspaces.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Enums;

public class GlobalQueryFilterTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public GlobalQueryFilterTests()
    {
        // Keep in-memory SQLite connection open for the lifetime of this test instance
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .EnableDetailedErrors()
            .EnableSensitiveDataLogging()
            .Options;

        // Build the database schema in memory
        using var initContext = new ApplicationDbContext(_options, new WorkspaceContext());
        initContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task GivenNoActiveWorkspace_WhenQueryingWorkspaceEntities_ReturnsZeroRecords()
    {
        // Arrange: Seed database with a user, workspace, membership, and connection
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var seedContext = new WorkspaceContext();
        seedContext.SetContext(workspaceId, userId);

        using (var db = new ApplicationDbContext(_options, seedContext))
        {
            var user = new ApplicationUser { Id = userId, UserName = "owner", Email = "owner@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Engineering");
            var membership = WorkspaceMembership.Create(workspaceId, userId, WorkspaceRole.Owner);
            var connection = WorkspaceConnection.Create(workspaceId, "Slack", "Slack", "enc_token", userId);

            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.WorkspaceMemberships.Add(membership);
            db.WorkspaceConnections.Add(connection);
            await db.SaveChangesAsync();
        }

        // Act: Create context with NO active workspace (CurrentWorkspaceId is null)
        var unauthenticatedContext = new WorkspaceContext(); // Uninitialized -> CurrentWorkspaceId is null
        using var queryDb = new ApplicationDbContext(_options, unauthenticatedContext);

        // var workspaces = await queryDb.Workspaces.ToListAsync();
        var workspaces = await queryDb.Workspaces.ToListAsync();
        var memberships = await queryDb.WorkspaceMemberships.ToListAsync();
        var connections = await queryDb.WorkspaceConnections.ToListAsync();

        // Assert: Fail-closed verification
        Assert.Empty(workspaces);
        Assert.Empty(memberships);
        Assert.Empty(connections);
    }
    public void Dispose()
    {
        _connection.Dispose();
    }
}