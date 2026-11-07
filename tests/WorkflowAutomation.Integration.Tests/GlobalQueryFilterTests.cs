namespace WorkflowAutomation.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Infrastructure.Persistence;
using WorkflowAutomation.Integration.Tests.Fixtures;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Workspaces.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Enums;

public class GlobalQueryFilterTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public GlobalQueryFilterTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenNoActiveWorkspace_WhenQueryingWorkspaceEntities_ReturnsZeroRecords()
    {
        // Arrange: Seed database with a user, workspace, membership, and connection
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using (var scope = _fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            context.SetContext(workspaceId, userId);


            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
        using (var queryScope = _fixture.CreateScope())
        {
            var queryDb = queryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workspaces = await queryDb.Workspaces.ToListAsync();
            var memberships = await queryDb.WorkspaceMemberships.ToListAsync();
            var connections = await queryDb.WorkspaceConnections.ToListAsync();

            // Assert: Fail-closed verification
            Assert.Empty(workspaces);
            Assert.Empty(memberships);
            Assert.Empty(connections);
        }

    }

    [Fact]
    public async Task GivenActiveWorkspace_WhenQueryingEntities_ReturnsOnlyActiveWorkspaceRecords()
    {
        // Arrange: Seed two distinct workspaces (A and B) with their own users, memberships, and connections
        var workspaceAId = Guid.NewGuid();
        var userAId = Guid.NewGuid();

        var workspaceBId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        using (var scopeA = _fixture.CreateScope())
        {
            var ctxA = scopeA.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctxA.SetContext(workspaceAId, userAId);
            var db = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userA = new ApplicationUser { Id = userAId, UserName = "alice", Email = "alice@a.com" };

            var workspaceA = Workspace.CreatePersonal(workspaceAId, userAId, "Workspace A");
            var membershipA = WorkspaceMembership.Create(workspaceAId, userAId, WorkspaceRole.Owner);
            var connectionA = WorkspaceConnection.Create(workspaceAId, "Slack A", "Slack", "enc_token_a", userAId);

            db.Users.Add(userA);
            db.Workspaces.Add(workspaceA);
            db.WorkspaceMemberships.Add(membershipA);
            db.WorkspaceConnections.Add(connectionA);
            await db.SaveChangesAsync();
        }

        using (var scopeB = _fixture.CreateScope())
        {
            var ctxB = scopeB.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctxB.SetContext(workspaceBId, userBId);
            var userB = new ApplicationUser { Id = userBId, UserName = "bob", Email = "bob@b.com" };
            var workspaceB = Workspace.CreatePersonal(workspaceBId, userBId, "Workspace B");
            var membershipB = WorkspaceMembership.Create(workspaceBId, userBId, WorkspaceRole.Owner);
            var connectionB = WorkspaceConnection.Create(workspaceBId, "GitHub B", "GitHub", "enc_token_b", userBId);
            var db = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(userB);
            db.WorkspaceMemberships.Add(membershipB);
            db.WorkspaceConnections.Add(connectionB);
            db.Workspaces.Add(workspaceB);
            db.SaveChanges();
        }

        // Act: Query as Workspace A
        using (var queryScope = _fixture.CreateScope())
        {
            var queryContext = queryScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            queryContext.SetContext(workspaceAId, userAId);
            var queryDb = queryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var workspaces = await queryDb.Workspaces.ToListAsync();
            var memberships = await queryDb.WorkspaceMemberships.ToListAsync();
            var connections = await queryDb.WorkspaceConnections.ToListAsync();

            // Assert: Workspace A sees only its own records
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

        // 1. Seed Alice and her first workspace
        using (var scopeAlice1 = _fixture.CreateScope())
        {
            var ctx = scopeAlice1.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx.SetContext(wsAlice1Id, aliceId);
            var db = scopeAlice1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var alice = new ApplicationUser { Id = aliceId, UserName = "alice", Email = "alice@test.com" };
            var ws1 = Workspace.CreatePersonal(wsAlice1Id, aliceId, "Alice WS 1");
            var mem1 = WorkspaceMembership.Create(wsAlice1Id, aliceId, WorkspaceRole.Owner);

            db.Users.Add(alice);
            db.Workspaces.Add(ws1);
            db.WorkspaceMemberships.Add(mem1);
            await db.SaveChangesAsync();
        }

        // 2. Seed Alice's second active workspace
        using (var scopeAlice2 = _fixture.CreateScope())
        {
            var ctx = scopeAlice2.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx.SetContext(wsAlice2Id, aliceId);
            var db = scopeAlice2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ws2 = Workspace.CreatePersonal(wsAlice2Id, aliceId, "Alice WS 2");
            var mem2 = WorkspaceMembership.Create(wsAlice2Id, aliceId, WorkspaceRole.Member);

            db.Workspaces.Add(ws2);
            db.WorkspaceMemberships.Add(mem2);
            await db.SaveChangesAsync();
        }

        // 3. Seed Alice's deactivated workspace
        using (var scopeAliceInactive = _fixture.CreateScope())
        {
            var ctx = scopeAliceInactive.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx.SetContext(wsAliceInactiveId, aliceId);
            var db = scopeAliceInactive.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var wsInactive = Workspace.CreatePersonal(wsAliceInactiveId, aliceId, "Alice Inactive WS");
            var memInactive = WorkspaceMembership.Create(wsAliceInactiveId, aliceId, WorkspaceRole.Viewer);
            memInactive.Deactivate();

            db.Workspaces.Add(wsInactive);
            db.WorkspaceMemberships.Add(memInactive);
            await db.SaveChangesAsync();
        }

        // 4. Seed Bob and his workspace
        using (var scopeB = _fixture.CreateScope())
        {
            var ctxB = scopeB.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctxB.SetContext(wsBobId, bobId);
            var bob = new ApplicationUser { Id = bobId, UserName = "bob", Email = "bob@test.com" };
            var db = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wsBob = Workspace.CreatePersonal(wsBobId, bobId, "Bob WS");
            var memBob = WorkspaceMembership.Create(wsBobId, bobId, WorkspaceRole.Owner);
            db.Users.Add(bob);
            db.Workspaces.Add(wsBob);
            db.WorkspaceMemberships.Add(memBob);
            await db.SaveChangesAsync();
            var memberships = await db.WorkspaceMemberships.ToListAsync();
            Assert.Single(memberships);
            Assert.Equal(wsBobId, memberships[0].WorkspaceId);
            Assert.Equal(bobId, memberships[0].UserId);
        }

        // Act & Assert 1: Ordinary query without workspace context returns 0 (fail-closed verified)
        using (var queryScope = _fixture.CreateScope())
        {
            var unauthenticatedContext = new WorkspaceContext(); // CurrentWorkspaceId is null
            var queryDb = queryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
    }

}