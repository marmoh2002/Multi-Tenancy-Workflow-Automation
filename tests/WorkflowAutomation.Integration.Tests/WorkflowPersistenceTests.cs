namespace WorkflowAutomation.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Infrastructure.Persistence;
using WorkflowAutomation.Integration.Tests.Fixtures;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Shared.Kernel.Time;
using WorkflowAutomation.Workflows.Core;
using WorkflowAutomation.Workflows.Core.Definitions;
using WorkflowAutomation.Workflows.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Entities;
using Xunit;

public class WorkflowPersistenceTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly SystemClock _clock = new();
    private readonly KahnDagValidator _validator = new();

    public WorkflowPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenValidWorkflowAndVersion_WhenSaved_CanBeRetrievedInSeparateScope()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        // 1. Arrange & Act: Seed and persist in Scope 1
        using (var scope = _fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            context.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Satisfy prerequisites: AspNetUsers & Workspaces
            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
            db.Users.Add(user);
            db.Workspaces.Add(workspace);

            // Create Workflow and draft WorkflowVersion
            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Customer Onboarding", "Onboarding flow", _clock);
            var definition = new WorkflowDefinition
            {
                Steps = [new StepDefinition { Id = "step-1", Type = "SendEmail" }]
            };
            var version = WorkflowVersion.CreateDraft(versionId, workspaceId, workflowId, 1, definition, _validator, _clock);

            db.Workflows.Add(workflow);
            db.WorkflowVersions.Add(version);

            await db.SaveChangesAsync();
        }

        // 2. Assert: Query back from a fresh, isolated Scope 2
        using (var verifyScope = _fixture.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            verifyContext.SetContext(workspaceId, userId);
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var persistedWorkflow = await verifyDb.Workflows.FindAsync(workflowId);
            Assert.NotNull(persistedWorkflow);
            Assert.Equal("Customer Onboarding", persistedWorkflow.Name);

            var persistedVersion = await verifyDb.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(persistedVersion);
            Assert.Equal(1, persistedVersion.VersionNumber);
            Assert.False(persistedVersion.IsPublished);

            // Verify jsonb round-trip and deserialization
            var roundTripDefinition = persistedVersion.GetDefinition();
            Assert.NotNull(roundTripDefinition);
            Assert.Single(roundTripDefinition.Steps);
            Assert.Equal("step-1", roundTripDefinition.Steps[0].Id);
            Assert.Equal("SendEmail", roundTripDefinition.Steps[0].Type);
        }
    }
    [Fact]
    public async Task GivenPublishedVersion_WhenActivated_WorkflowAndActiveVersionArePersisted()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        // 1. Arrange & Act: Create, publish, activate, and persist in Scope 1
        using (var scope = _fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            context.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
            db.Users.Add(user);
            db.Workspaces.Add(workspace);

            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Invoice Processing", "Processes invoices", _clock);
            var definition = new WorkflowDefinition
            {
                Steps = [new StepDefinition { Id = "step-1", Type = "ParseInvoice" }]
            };
            var version = WorkflowVersion.CreateDraft(versionId, workspaceId, workflowId, 1, definition, _validator, _clock);

            // Domain lifecycle: publish version, persist the version, then activate the workflow
            version.Publish(new KahnDagValidator(), _clock);

            db.Workflows.Add(workflow);
            db.WorkflowVersions.Add(version);

            await db.SaveChangesAsync();

            workflow.ActivateVersion(version, _clock);
            workflow.Activate(_clock);

            await db.SaveChangesAsync();
        }

        // 2. Assert: Verify in a fresh Scope 2
        using (var verifyScope = _fixture.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            verifyContext.SetContext(workspaceId, userId);
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var persistedWorkflow = await verifyDb.Workflows.FindAsync(workflowId);
            Assert.NotNull(persistedWorkflow);
            Assert.True(persistedWorkflow.IsActive);
            Assert.Equal(versionId, persistedWorkflow.ActiveVersionId);

            var persistedVersion = await verifyDb.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(persistedVersion);
            Assert.True(persistedVersion.IsPublished);
            Assert.NotNull(persistedVersion.PublishedAt);
        }
    }
    [Fact]
    public async Task GivenDuplicateWorkflowNameInSameWorkspace_WhenSaved_ThrowsDbUpdateException()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var scope = _fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        context.SetContext(workspaceId, userId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
        var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
        db.Users.Add(user);
        db.Workspaces.Add(workspace);

        var workflow1 = Workflow.Create(Guid.NewGuid(), workspaceId, userId, "Customer Onboarding", null, _clock);
        var workflow2 = Workflow.Create(Guid.NewGuid(), workspaceId, userId, "Customer Onboarding", null, _clock);

        db.Workflows.Add(workflow1);
        db.Workflows.Add(workflow2);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task GivenDuplicateVersionNumberForSameWorkflow_WhenSaved_ThrowsDbUpdateException()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        using var scope = _fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        context.SetContext(workspaceId, userId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
        var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
        db.Users.Add(user);
        db.Workspaces.Add(workspace);

        var workflow = Workflow.Create(workflowId, workspaceId, userId, "Data Sync Flow", null, _clock);
        db.Workflows.Add(workflow);

        var definition1 = new WorkflowDefinition
        {
            Steps = [new StepDefinition { Id = "step-1", Type = "FetchData" }]
        };
        var definition2 = new WorkflowDefinition
        {
            Steps = [new StepDefinition { Id = "step-1", Type = "SyncData" }]
        };

        var version1 = WorkflowVersion.CreateDraft(Guid.NewGuid(), workspaceId, workflowId, 1, definition1, _validator, _clock);
        var version2 = WorkflowVersion.CreateDraft(Guid.NewGuid(), workspaceId, workflowId, 1, definition2, _validator, _clock);

        db.WorkflowVersions.Add(version1);
        db.WorkflowVersions.Add(version2);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task GivenVersionBelongingToDifferentWorkspaceThanWorkflow_WhenSaved_ThrowsDbUpdateException()
    {
        var workspace1Id = Guid.NewGuid();
        var workspace2Id = Guid.NewGuid();
        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();
        var workflow1Id = Guid.NewGuid();

        // 1. Arrange: Persist Workflow 1 in Workspace 1
        using (var scope1 = _fixture.CreateScope())
        {
            var context1 = scope1.ServiceProvider.GetRequiredService<WorkspaceContext>();
            context1.SetContext(workspace1Id, user1Id);
            var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user1 = new ApplicationUser { Id = user1Id, UserName = $"user_{user1Id:N}", Email = $"{user1Id:N}@test.com" };
            var workspace1 = Workspace.CreatePersonal(workspace1Id, user1Id, "Workspace 1");
            var workflow1 = Workflow.Create(workflow1Id, workspace1Id, user1Id, "Tenant 1 Workflow", null, _clock);

            db1.Users.Add(user1);
            db1.Workspaces.Add(workspace1);
            db1.Workflows.Add(workflow1);
            await db1.SaveChangesAsync();
        }

        // 2. Act: In Workspace 2, attempt to attach a version to Workflow 1
        using (var scope2 = _fixture.CreateScope())
        {
            var context2 = scope2.ServiceProvider.GetRequiredService<WorkspaceContext>();
            context2.SetContext(workspace2Id, user2Id);
            var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user2 = new ApplicationUser { Id = user2Id, UserName = $"user_{user2Id:N}", Email = $"{user2Id:N}@test.com" };
            var workspace2 = Workspace.CreatePersonal(workspace2Id, user2Id, "Workspace 2");
            db2.Users.Add(user2);
            db2.Workspaces.Add(workspace2);
            await db2.SaveChangesAsync();

            var definition = new WorkflowDefinition
            {
                Steps = [new StepDefinition { Id = "step-1", Type = "Action" }]
            };

            // Version has WorkspaceId = workspace2Id, but targets workflow1Id (from workspace 1)
            var crossTenantVersion = WorkflowVersion.CreateDraft(
                Guid.NewGuid(),
                workspace2Id,
                workflow1Id,
                1,
                definition,
                _validator,
                _clock);

            db2.WorkflowVersions.Add(crossTenantVersion);

            // 3. Assert: Composite FK (WorkspaceId, WorkflowId) -> Workflows(WorkspaceId, Id) rejects it
            await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        }
    }
    [Fact]
    public async Task GivenActiveVersionBelongingToDifferentWorkflow_WhenSaved_ThrowsDbUpdateException()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflow1Id = Guid.NewGuid();
        var workflow2Id = Guid.NewGuid();
        var version2Id = Guid.NewGuid();

        using var scope = _fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        context.SetContext(workspaceId, userId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
        var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
        db.Users.Add(user);
        db.Workspaces.Add(workspace);

        // 1. Arrange: Create two workflows and a version belonging to Workflow 2
        var workflow1 = Workflow.Create(workflow1Id, workspaceId, userId, "Workflow 1", null, _clock);
        var workflow2 = Workflow.Create(workflow2Id, workspaceId, userId, "Workflow 2", null, _clock);

        var definition = new WorkflowDefinition
        {
            Steps = [new StepDefinition { Id = "step-1", Type = "Action" }]
        };
        var version2 = WorkflowVersion.CreateDraft(version2Id, workspaceId, workflow2Id, 1, definition, _validator, _clock);

        db.Workflows.Add(workflow1);
        db.Workflows.Add(workflow2);
        db.WorkflowVersions.Add(version2);
        await db.SaveChangesAsync();

        // 2. Act: Force Workflow 1's ActiveVersionId to point to Workflow 2's version
        db.Entry(workflow1).Property(w => w.ActiveVersionId).CurrentValue = version2Id;

        // 3. Assert: Composite FK (Id, ActiveVersionId) -> WorkflowVersions(WorkflowId, Id) rejects it
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task GivenMultipleWorkspaces_WhenQueryingWorkflowsAndVersions_ReturnsOnlyActiveWorkspaceRecords()
    {
        var ws1Id = Guid.NewGuid();
        var ws2Id = Guid.NewGuid();
        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();
        var wf1Id = Guid.NewGuid();
        var wf2Id = Guid.NewGuid();

        // 1. Arrange: Seed Workspace 1 with Workflow 1 & Version 1
        using (var scope1 = _fixture.CreateScope())
        {
            var ctx1 = scope1.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx1.SetContext(ws1Id, user1Id);
            var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user1 = new ApplicationUser { Id = user1Id, UserName = $"user_{user1Id:N}", Email = $"{user1Id:N}@test.com" };
            var ws1 = Workspace.CreatePersonal(ws1Id, user1Id, "Workspace 1");
            var wf1 = Workflow.Create(wf1Id, ws1Id, user1Id, "Workflow 1", null, _clock);
            var def1 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s1", Type = "Action1" }] };
            var v1 = WorkflowVersion.CreateDraft(Guid.NewGuid(), ws1Id, wf1Id, 1, def1, _validator, _clock);

            db1.Users.Add(user1);
            db1.Workspaces.Add(ws1);
            db1.Workflows.Add(wf1);
            db1.WorkflowVersions.Add(v1);
            await db1.SaveChangesAsync();
        }

        // Seed Workspace 2 with Workflow 2 & Version 2
        using (var scope2 = _fixture.CreateScope())
        {
            var ctx2 = scope2.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx2.SetContext(ws2Id, user2Id);
            var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user2 = new ApplicationUser { Id = user2Id, UserName = $"user_{user2Id:N}", Email = $"{user2Id:N}@test.com" };
            var ws2 = Workspace.CreatePersonal(ws2Id, user2Id, "Workspace 2");
            var wf2 = Workflow.Create(wf2Id, ws2Id, user2Id, "Workflow 2", null, _clock);
            var def2 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s2", Type = "Action2" }] };
            var v2 = WorkflowVersion.CreateDraft(Guid.NewGuid(), ws2Id, wf2Id, 1, def2, _validator, _clock);

            db2.Users.Add(user2);
            db2.Workspaces.Add(ws2);
            db2.Workflows.Add(wf2);
            db2.WorkflowVersions.Add(v2);
            await db2.SaveChangesAsync();
        }

        // 2. Act: Query as Workspace 1
        using (var queryScope = _fixture.CreateScope())
        {
            var queryCtx = queryScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            queryCtx.SetContext(ws1Id, user1Id);
            var queryDb = queryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workflows = await queryDb.Workflows.ToListAsync();
            var versions = await queryDb.WorkflowVersions.ToListAsync();

            // 3. Assert: All returned records belong exclusively to Workspace 1
            Assert.All(workflows, w => Assert.Equal(ws1Id, w.WorkspaceId));
            Assert.Contains(workflows, w => w.Id == wf1Id);
            Assert.DoesNotContain(workflows, w => w.Id == wf2Id);

            Assert.All(versions, v => Assert.Equal(ws1Id, v.WorkspaceId));
            Assert.Contains(versions, v => v.WorkflowId == wf1Id);
            Assert.DoesNotContain(versions, v => v.WorkflowId == wf2Id);
        }
    }
    [Fact]
    public async Task GivenNoActiveWorkspace_WhenQueryingWorkflowsAndVersions_ReturnsZeroRecords()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        // 1. Arrange: Seed a workflow and version under an active workspace
        using (var seedScope = _fixture.CreateScope())
        {
            var seedCtx = seedScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            seedCtx.SetContext(workspaceId, userId);
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Background Workflow", null, _clock);
            var definition = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s1", Type = "Action" }] };
            var version = WorkflowVersion.CreateDraft(Guid.NewGuid(), workspaceId, workflowId, 1, definition, _validator, _clock);

            seedDb.Users.Add(user);
            seedDb.Workspaces.Add(workspace);
            seedDb.Workflows.Add(workflow);
            seedDb.WorkflowVersions.Add(version);
            await seedDb.SaveChangesAsync();
        }

        // 2. Act: Query without establishing a workspace context (CurrentWorkspaceId is null)
        using (var unauthenticatedScope = _fixture.CreateScope())
        {
            // Note: We deliberately do NOT call context.SetContext(...)
            var queryDb = unauthenticatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workflows = await queryDb.Workflows.ToListAsync();
            var versions = await queryDb.WorkflowVersions.ToListAsync();

            // 3. Assert: Fail-closed (0 records returned, no exception thrown)
            Assert.Empty(workflows);
            Assert.Empty(versions);
        }
    }
    [Fact]
    public async Task GivenWorkflowWithVersions_WhenWorkflowDeleted_VersionsAreCascadeDeleted()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var version1Id = Guid.NewGuid();
        var version2Id = Guid.NewGuid();

        // 1. Arrange: Persist a workflow with two versions
        using (var scope1 = _fixture.CreateScope())
        {
            var ctx1 = scope1.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx1.SetContext(workspaceId, userId);
            var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Ephemeral Workflow", null, _clock);

            var def1 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s1", Type = "Action1" }] };
            var def2 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s2", Type = "Action2" }] };
            var v1 = WorkflowVersion.CreateDraft(version1Id, workspaceId, workflowId, 1, def1, _validator, _clock);
            var v2 = WorkflowVersion.CreateDraft(version2Id, workspaceId, workflowId, 2, def2, _validator, _clock);

            db1.Users.Add(user);
            db1.Workspaces.Add(workspace);
            db1.Workflows.Add(workflow);
            db1.WorkflowVersions.AddRange(v1, v2);
            await db1.SaveChangesAsync();
        }

        // 2. Act: In an isolated scope, load and delete ONLY the workflow entity
        using (var deleteScope = _fixture.CreateScope())
        {
            var deleteCtx = deleteScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            deleteCtx.SetContext(workspaceId, userId);
            var deleteDb = deleteScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workflowToDelete = await deleteDb.Workflows.FindAsync(workflowId);
            Assert.NotNull(workflowToDelete);

            deleteDb.Workflows.Remove(workflowToDelete);
            await deleteDb.SaveChangesAsync();
        }

        // 3. Assert: In a fresh scope, confirm workflow and both versions are gone from PostgreSQL
        using (var verifyScope = _fixture.CreateScope())
        {
            var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            verifyCtx.SetContext(workspaceId, userId);
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var persistedWorkflow = await verifyDb.Workflows.FindAsync(workflowId);
            Assert.Null(persistedWorkflow);

            var persistedVersions = await verifyDb.WorkflowVersions
                .Where(v => v.WorkflowId == workflowId)
                .ToListAsync();

            Assert.Empty(persistedVersions);
        }
    }
    [Fact]
    public async Task GivenWorkspaceWithWorkflows_WhenWorkspaceDeleted_ThrowsDbUpdateException()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        // 1. Arrange: Persist a workspace and a workflow inside it
        using (var scope1 = _fixture.CreateScope())
        {
            var ctx1 = scope1.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx1.SetContext(workspaceId, userId);
            var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Protected Workspace");
            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Critical Workflow", null, _clock);

            db1.Users.Add(user);
            db1.Workspaces.Add(workspace);
            db1.Workflows.Add(workflow);
            await db1.SaveChangesAsync();
        }

        // 2. Act & Assert: In an isolated scope, attempt to delete the workspace
        using (var deleteScope = _fixture.CreateScope())
        {
            var deleteCtx = deleteScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            deleteCtx.SetContext(workspaceId, userId);
            var deleteDb = deleteScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workspaceToDelete = await deleteDb.Workspaces.FindAsync(workspaceId);
            Assert.NotNull(workspaceToDelete);

            deleteDb.Workspaces.Remove(workspaceToDelete);

            // Deletion must be rejected because workflows still reference this workspace
            await Assert.ThrowsAsync<DbUpdateException>(() => deleteDb.SaveChangesAsync());
        }
    }

}
