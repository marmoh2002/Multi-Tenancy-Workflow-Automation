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

    [Fact]
    public async Task GivenActiveWorkspace_WhenQueryingEntities_ReturnsOnlyActiveWorkspaceRecords()
    {
        // Arrange: Seed two distinct workspaces (A and B) with their own users, memberships, and connections
        var workspaceAId = Guid.NewGuid();
        var userAId = Guid.NewGuid();

        var workspaceBId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        // Seed using an unconstrained context or direct inserts
        var seedContext = new WorkspaceContext();
        seedContext.SetContext(workspaceAId, userAId);

        using (var db = new ApplicationDbContext(_options, seedContext))
        {
            var userA = new ApplicationUser { Id = userAId, UserName = "alice", Email = "alice@a.com" };
            var userB = new ApplicationUser { Id = userBId, UserName = "bob", Email = "bob@b.com" };

            var workspaceA = Workspace.CreatePersonal(workspaceAId, userAId, "Workspace A");
            var membershipA = WorkspaceMembership.Create(workspaceAId, userAId, WorkspaceRole.Owner);
            var connectionA = WorkspaceConnection.Create(workspaceAId, "Slack A", "Slack", "enc_token_a", userAId);

            var workspaceB = Workspace.CreatePersonal(workspaceBId, userBId, "Workspace B");
            var membershipB = WorkspaceMembership.Create(workspaceBId, userBId, WorkspaceRole.Owner);
            var connectionB = WorkspaceConnection.Create(workspaceBId, "GitHub B", "GitHub", "enc_token_b", userBId);

            db.Users.AddRange(userA, userB);
            db.Workspaces.AddRange(workspaceA, workspaceB);
            db.WorkspaceMemberships.AddRange(membershipA, membershipB);
            db.WorkspaceConnections.AddRange(connectionA, connectionB);
            await db.SaveChangesAsync();
        }

        // Act: Execute queries as Workspace A
        var contextA = new WorkspaceContext();
        contextA.SetContext(workspaceAId, userAId);

        using var queryDb = new ApplicationDbContext(_options, contextA);

        var workspaces = await queryDb.Workspaces.ToListAsync();
        var memberships = await queryDb.WorkspaceMemberships.ToListAsync();
        var connections = await queryDb.WorkspaceConnections.ToListAsync();

        // Assert: Workspace A sees its own records, and ZERO records belonging to Workspace B
        var foundWorkspace = Assert.Single(workspaces);
        Assert.Equal(workspaceAId, foundWorkspace.Id);
        Assert.Equal("Workspace A", foundWorkspace.Name);

        var foundMembership = Assert.Single(memberships);
        Assert.Equal(workspaceAId, foundMembership.WorkspaceId);
        Assert.Equal(userAId, foundMembership.UserId);

        var foundConnection = Assert.Single(connections);
        Assert.Equal(workspaceAId, foundConnection.WorkspaceId);
        Assert.Equal("Slack A", foundConnection.Name);
    }
    [Fact]
    public async Task GivenNoActiveWorkspace_WhenUsingIgnoreQueryFilters_UserCanOnlyRetrieveOwnActiveMemberships()
    {
        // Arrange: Seed Alice with 2 active workspaces and 1 deactivated workspace, and Bob with 1 workspace
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();

        var wsAlice1Id = Guid.NewGuid();
        var wsAlice2Id = Guid.NewGuid();
        var wsAliceInactiveId = Guid.NewGuid();
        var wsBobId = Guid.NewGuid();

        var seedContext = new WorkspaceContext();
        seedContext.SetContext(wsAlice1Id, aliceId);

        using (var db = new ApplicationDbContext(_options, seedContext))
        {
            var alice = new ApplicationUser { Id = aliceId, UserName = "alice", Email = "alice@test.com" };
            var bob = new ApplicationUser { Id = bobId, UserName = "bob", Email = "bob@test.com" };

            var ws1 = Workspace.CreatePersonal(wsAlice1Id, aliceId, "Alice WS 1");
            var ws2 = Workspace.CreatePersonal(wsAlice2Id, aliceId, "Alice WS 2");
            var wsInactive = Workspace.CreatePersonal(wsAliceInactiveId, aliceId, "Alice Inactive WS");
            var wsBob = Workspace.CreatePersonal(wsBobId, bobId, "Bob WS");

            var mem1 = WorkspaceMembership.Create(wsAlice1Id, aliceId, WorkspaceRole.Owner);
            var mem2 = WorkspaceMembership.Create(wsAlice2Id, aliceId, WorkspaceRole.Member);
            var memInactive = WorkspaceMembership.Create(wsAliceInactiveId, aliceId, WorkspaceRole.Viewer);
            memInactive.Deactivate(); // Alice was removed from this workspace

            var memBob = WorkspaceMembership.Create(wsBobId, bobId, WorkspaceRole.Owner);

            db.Users.AddRange(alice, bob);
            db.Workspaces.AddRange(ws1, ws2, wsInactive, wsBob);
            db.WorkspaceMemberships.AddRange(mem1, mem2, memInactive, memBob);
            await db.SaveChangesAsync();
        }

        // Act & Assert 1: Ordinary query without workspace context returns 0 (fail-closed verified)
        var unauthenticatedContext = new WorkspaceContext(); // CurrentWorkspaceId is null
        using var queryDb = new ApplicationDbContext(_options, unauthenticatedContext);

        var ordinaryMemberships = await queryDb.WorkspaceMemberships
            .Where(m => m.UserId == aliceId)
            .ToListAsync();

        Assert.Empty(ordinaryMemberships);

        // Act & Assert 2: Controlled Bootstrap Query with IgnoreQueryFilters()
        // Must explicitly restrict by trusted UserId and IsActive == true
        var bootstrapMemberships = await queryDb.WorkspaceMemberships
            .IgnoreQueryFilters()
            .Where(m => m.UserId == aliceId && m.IsActive)
            .ToListAsync();

        // Alice sees exactly her 2 active workspaces
        Assert.Equal(2, bootstrapMemberships.Count);
        Assert.All(bootstrapMemberships, m => Assert.Equal(aliceId, m.UserId));
        Assert.Contains(bootstrapMemberships, m => m.WorkspaceId == wsAlice1Id);
        Assert.Contains(bootstrapMemberships, m => m.WorkspaceId == wsAlice2Id);

        // Verify Bob's workspace and the inactive workspace are completely excluded
        Assert.DoesNotContain(bootstrapMemberships, m => m.WorkspaceId == wsBobId);
        Assert.DoesNotContain(bootstrapMemberships, m => m.WorkspaceId == wsAliceInactiveId);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}