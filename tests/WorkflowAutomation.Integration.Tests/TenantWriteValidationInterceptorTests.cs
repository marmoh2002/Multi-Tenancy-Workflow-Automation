namespace WorkflowAutomation.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Infrastructure.Interceptors;
using WorkflowAutomation.Infrastructure.Persistence;
using WorkflowAutomation.Integration.Tests.Fixtures;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Workspaces.Core.Entities;
using Xunit;

public class TenantWriteValidationInterceptorTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public TenantWriteValidationInterceptorTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenMatchingWorkspace_WhenSavingChangesAsync_EntityIsPersisted()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        using var scope = _fixture.CreateScope();
        var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        workspaceContext.SetContext(workspaceId, userId);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
        var workspace = Workspace.CreatePersonal(workspaceId, userId, "Engineering WS");
        var connection = WorkspaceConnection.Create(connectionId, workspaceId, "Slack Conn", "Slack", "enc_token", userId);

        db.Users.Add(user);
        db.Workspaces.Add(workspace);
        db.WorkspaceConnections.Add(connection);

        // Act
        var savedCount = await db.SaveChangesAsync();

        // Assert
        Assert.True(savedCount > 0);

        // Verification query using a fresh scope
        using var verifyScope = _fixture.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        verifyContext.SetContext(workspaceId, userId);
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var persistedConnection = await verifyDb.WorkspaceConnections.FindAsync(connectionId);
        Assert.NotNull(persistedConnection);
        Assert.Equal(workspaceId, persistedConnection.WorkspaceId);
    }

    [Fact]
    public async Task GivenMismatchedWorkspace_WhenSavingChangesAsync_ThrowsExceptionAndDoesNotPersist()
    {
        // Arrange: Prepare seed data for both Workspace A and Workspace B
        var workspaceAId = Guid.NewGuid();
        var workspaceBId = Guid.NewGuid();
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        // Seed prerequisites (users and workspaces)
        using (var seedScope = _fixture.CreateScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            seedContext.SetContext(workspaceAId, userAId);
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            seedDb.Users.AddRange(
                new ApplicationUser { Id = userAId, UserName = $"user_{userAId:N}", Email = $"{userAId:N}@test.com" },
                new ApplicationUser { Id = userBId, UserName = $"user_{userBId:N}", Email = $"{userBId:N}@test.com" }
            );
            seedDb.Workspaces.AddRange(
                Workspace.CreatePersonal(workspaceAId, userAId, "Workspace A"),
                Workspace.CreatePersonal(workspaceBId, userBId, "Workspace B")
            );
            await seedDb.SaveChangesAsync();
        }

        // Act: In Scope A, attempt to insert a connection belonging to Workspace B
        var invalidConnectionId = Guid.NewGuid();
        using var scopeA = _fixture.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<WorkspaceContext>();
        contextA.SetContext(workspaceAId, userAId);

        var dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crossWorkspaceConnection = WorkspaceConnection.Create(
            invalidConnectionId,
            workspaceBId, // Belongs to B, but scope is operating in A!
            "Mismatched Slack",
            "Slack",
            "enc_token",
            userAId);

        dbA.WorkspaceConnections.Add(crossWorkspaceConnection);

        // Assert: Specific validation exception must be thrown
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => dbA.SaveChangesAsync());
        Assert.Contains("Cross-workspace write detected", ex.Message);
        Assert.Contains("WorkspaceConnection", ex.Message);
        Assert.Contains(workspaceBId.ToString(), ex.Message);
        Assert.Contains(workspaceAId.ToString(), ex.Message);

        // Verification: Use IgnoreQueryFilters() in a fresh scope to prove it was never written to PostgreSQL
        using var verifyScope = _fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existsInDb = await verifyDb.WorkspaceConnections
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == invalidConnectionId);

        Assert.False(existsInDb, "Entity rejected by interceptor must not be persisted in the database.");
    }

    [Fact]
    public void GivenMismatchedWorkspace_WhenSavingChangesSync_ThrowsExceptionAndDoesNotPersist()
    {
        // Arrange
        var workspaceAId = Guid.NewGuid();
        var workspaceBId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        using var scopeA = _fixture.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<WorkspaceContext>();
        contextA.SetContext(workspaceAId, userId);

        var dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crossWorkspaceConnection = WorkspaceConnection.Create(
            connectionId,
            workspaceBId,
            "Sync Check",
            "Slack",
            "enc_token",
            userId);

        dbA.WorkspaceConnections.Add(crossWorkspaceConnection);

        // Act & Assert (Synchronous SaveChanges)
        var ex = Assert.Throws<InvalidOperationException>(() => dbA.SaveChanges());
        Assert.Contains("Cross-workspace write detected", ex.Message);

        // Verification
        using var verifyScope = _fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exists = verifyDb.WorkspaceConnections
            .IgnoreQueryFilters()
            .Any(c => c.Id == connectionId);

        Assert.False(exists, "Synchronously rejected entity must not be persisted.");
    }

    [Fact]
    public async Task GivenNoActiveWorkspace_WhenSavingTenantEntity_ThrowsExceptionAndDoesNotPersist()
    {
        // Arrange: Uninitialized WorkspaceContext (CurrentWorkspaceId is null)
        var connectionId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var unauthenticatedScope = _fixture.CreateScope();
        var db = unauthenticatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var connection = WorkspaceConnection.Create(connectionId, workspaceId, "No Auth", "Slack", "enc_token", userId);
        db.WorkspaceConnections.Add(connection);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal("Cannot mutate workspace-scoped entities without an active workspace context.", ex.Message);

        // Verification
        using var verifyScope = _fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exists = await verifyDb.WorkspaceConnections
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == connectionId);

        Assert.False(exists, "Unauthenticated write must not be persisted.");
    }

    [Fact]
    public async Task GivenScopeAUsed_WhenScopeBRunsSubsequently_ScopeBSuccessfullySavesWorkspaceBEntity()
    {
        // Arrange: Identifiers for Workspace A and Workspace B
        var workspaceAId = Guid.NewGuid();
        var userAId = Guid.NewGuid();
        var connAId = Guid.NewGuid();

        var workspaceBId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var connBId = Guid.NewGuid();

        // 1. Run Scope A
        using (var scopeA = _fixture.CreateScope())
        {
            var contextA = scopeA.ServiceProvider.GetRequiredService<WorkspaceContext>();
            contextA.SetContext(workspaceAId, userAId);
            var dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var userA = new ApplicationUser { Id = userAId, UserName = $"user_{userAId:N}", Email = $"{userAId:N}@test.com" };
            var wsA = Workspace.CreatePersonal(workspaceAId, userAId, "Workspace A");
            var connA = WorkspaceConnection.Create(connAId, workspaceAId, "Slack A", "Slack", "enc_token", userAId);

            dbA.Users.Add(userA);
            dbA.Workspaces.Add(wsA);
            dbA.WorkspaceConnections.Add(connA);
            await dbA.SaveChangesAsync();
        }

        // 2. Run Scope B immediately after Scope A has finished
        using (var scopeB = _fixture.CreateScope())
        {
            var contextB = scopeB.ServiceProvider.GetRequiredService<WorkspaceContext>();
            contextB.SetContext(workspaceBId, userBId);
            var dbB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var userB = new ApplicationUser { Id = userBId, UserName = $"user_{userBId:N}", Email = $"{userBId:N}@test.com" };
            var wsB = Workspace.CreatePersonal(workspaceBId, userBId, "Workspace B");
            var connB = WorkspaceConnection.Create(connBId, workspaceBId, "Slack B", "Slack", "enc_token", userBId);

            dbB.Users.Add(userB);
            dbB.Workspaces.Add(wsB);
            dbB.WorkspaceConnections.Add(connB);

            // Act: Scope B should NOT be contaminated by Workspace A
            var savedB = await dbB.SaveChangesAsync();

            // Assert
            Assert.True(savedB > 0);
        }

        // 3. Verify in database
        using (var verifyScope = _fixture.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var connB = await verifyDb.WorkspaceConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == connBId);

            Assert.NotNull(connB);
            Assert.Equal(workspaceBId, connB.WorkspaceId);
        }
    }

    [Fact]
    public void GivenSingleScope_WhenResolvingWorkspaceContextAndInterface_ReturnsExactSameReference()
    {
        // Act
        using var scope = _fixture.CreateScope();
        var concrete = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        var iface = scope.ServiceProvider.GetRequiredService<IWorkspaceContext>();

        // Assert: Must be the identical object in memory
        Assert.Same(concrete, iface);
    }

    [Fact]
    public void GivenDifferentScopes_WhenResolvingWorkspaceContextAndInterceptor_ReturnsDistinctInstances()
    {
        // Act
        using var scope1 = _fixture.CreateScope();
        using var scope2 = _fixture.CreateScope();

        var ctx1 = scope1.ServiceProvider.GetRequiredService<WorkspaceContext>();
        var ctx2 = scope2.ServiceProvider.GetRequiredService<WorkspaceContext>();

        var interceptor1 = scope1.ServiceProvider.GetRequiredService<TenantWriteValidationInterceptor>();
        var interceptor2 = scope2.ServiceProvider.GetRequiredService<TenantWriteValidationInterceptor>();

        // Assert: Scopes must never share context or interceptor instances
        Assert.NotSame(ctx1, ctx2);
        Assert.NotSame(interceptor1, interceptor2);
    }
}