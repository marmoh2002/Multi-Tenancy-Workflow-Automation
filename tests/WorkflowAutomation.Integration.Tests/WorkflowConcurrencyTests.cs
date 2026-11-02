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

public class WorkflowConcurrencyTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly SystemClock _clock = new();
    private readonly KahnDagValidator _validator = new();

    public WorkflowConcurrencyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private static WorkflowDefinition CreateSampleDefinition(string stepId = "step-1")
    {
        return new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition { Id = stepId, Type = "Log" }
            ]
        };
    }

    private async Task<(ApplicationUser au, Workspace ws, Workflow wf)> SeedPrerequisitesAsync(
        Guid? workspaceId = null,
        Guid? userId = null,
        Guid? workflowId = null)
    {
        var wsId = workspaceId ?? Guid.NewGuid();
        var uId = userId ?? Guid.NewGuid();
        var wfId = workflowId ?? Guid.NewGuid();

        var user = new ApplicationUser
        {
            Id = uId,
            UserName = $"user_{uId:N}",
            Email = $"{uId:N}@test.com"
        };
        var workspace = Workspace.CreatePersonal(wsId, uId, "Test Workspace");
        var workflow = Workflow.Create(wfId, wsId, uId, "Test Workflow", "Description", _clock);

        return (user, workspace, workflow);
    }

    [Fact]
    public async Task GivenConcurrentDraftEdits_WhenSecondScopeSaves_ThrowsDbUpdateConcurrencyException()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed draft version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync();
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var draft = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition("step-initial"),
                _validator,
                _clock);

            db.WorkflowVersions.Add(draft);
            await db.SaveChangesAsync();
        }

        // 2. Open two separate scopes and load the same draft in both
        using var scope1 = _fixture.CreateScope();
        using var scope2 = _fixture.CreateScope();

        scope1.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);
        scope2.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);

        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var draftInScope1 = await db1.WorkflowVersions.FindAsync(versionId);
        var draftInScope2 = await db2.WorkflowVersions.FindAsync(versionId);

        Assert.NotNull(draftInScope1);
        Assert.NotNull(draftInScope2);

        // 3. Scope 1 modifies draft and saves successfully
        draftInScope1.UpdateDraft(CreateSampleDefinition("step-from-scope-1"), _validator, _clock);
        await db1.SaveChangesAsync();

        // 4. Scope 2 modifies draft and attempts to save with stale xmin token
        draftInScope2.UpdateDraft(CreateSampleDefinition("step-from-scope-2"), _validator, _clock);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }
    [Fact]
    public async Task GivenDraftEditVersusPublication_WhenStaleEditSaves_ThrowsDbUpdateConcurrencyException()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed draft version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync();
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var draft = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition("step-initial"),
                _validator,
                _clock);

            db.WorkflowVersions.Add(draft);
            await db.SaveChangesAsync();
        }

        // 2. Open two separate scopes and load the same draft in both
        using var scopeEdit = _fixture.CreateScope();
        using var scopePublish = _fixture.CreateScope();

        scopeEdit.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);
        scopePublish.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);

        var dbEdit = scopeEdit.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbPublish = scopePublish.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var draftToEdit = await dbEdit.WorkflowVersions.FindAsync(versionId);
        var draftToPublish = await dbPublish.WorkflowVersions.FindAsync(versionId);

        Assert.NotNull(draftToEdit);
        Assert.NotNull(draftToPublish);

        // 3. The publish scope publishes the draft and saves
        draftToPublish.Publish(_validator, _clock);
        await dbPublish.SaveChangesAsync();

        // 4. The edit scope attempts to save a draft edit with a stale xmin token
        draftToEdit.UpdateDraft(CreateSampleDefinition("step-stale-edit"), _validator, _clock);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbEdit.SaveChangesAsync());
    }
    [Fact]
    public async Task GivenConcurrentPublication_WhenSecondScopeSaves_ThrowsDbUpdateConcurrencyException()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed draft version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync();
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var draft = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition("step-initial"),
                _validator,
                _clock);

            db.WorkflowVersions.Add(draft);
            await db.SaveChangesAsync();
        }

        // 2. Open two separate scopes and load the same draft in both
        using var scope1 = _fixture.CreateScope();
        using var scope2 = _fixture.CreateScope();

        scope1.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);
        scope2.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);

        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var draft1 = await db1.WorkflowVersions.FindAsync(versionId);
        var draft2 = await db2.WorkflowVersions.FindAsync(versionId);

        Assert.NotNull(draft1);
        Assert.NotNull(draft2);

        // 3. Scope 1 publishes and saves successfully
        draft1.Publish(_validator, _clock);
        await db1.SaveChangesAsync();

        // 4. Scope 2 also publishes and attempts to save
        draft2.Publish(_validator, _clock);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }
    [Fact]
    public async Task GivenDraftDeletionAfterConcurrentEdit_WhenStaleDeleteSaves_ThrowsDbUpdateConcurrencyException()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed draft version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync();
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var draft = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition("step-initial"),
                _validator,
                _clock);

            db.WorkflowVersions.Add(draft);
            await db.SaveChangesAsync();
        }

        // 2. Open two separate scopes and load the same draft in both
        using var scopeDelete = _fixture.CreateScope();
        using var scopeEdit = _fixture.CreateScope();

        scopeDelete.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);
        scopeEdit.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);

        var dbDelete = scopeDelete.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbEdit = scopeEdit.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var draftToDelete = await dbDelete.WorkflowVersions.FindAsync(versionId);
        var draftToEdit = await dbEdit.WorkflowVersions.FindAsync(versionId);

        Assert.NotNull(draftToDelete);
        Assert.NotNull(draftToEdit);

        // 3. Edit scope updates the draft and saves (advancing xmin in PostgreSQL)
        draftToEdit.UpdateDraft(CreateSampleDefinition("step-modified"), _validator, _clock);
        await dbEdit.SaveChangesAsync();

        // 4. Delete scope attempts to delete using stale xmin token
        dbDelete.WorkflowVersions.Remove(draftToDelete);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbDelete.SaveChangesAsync());
    }
    [Fact]
    public async Task GivenConcurrentWorkflowMetadataEdits_WhenSecondScopeSaves_ThrowsDbUpdateConcurrencyException()
    {
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed workflow
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync();
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            await db.SaveChangesAsync();
        }

        // 2. Open two separate scopes and load the same workflow in both
        using var scope1 = _fixture.CreateScope();
        using var scope2 = _fixture.CreateScope();

        scope1.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);
        scope2.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);

        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var workflowInScope1 = await db1.Workflows.FindAsync(workflow.Id);
        var workflowInScope2 = await db2.Workflows.FindAsync(workflow.Id);

        Assert.NotNull(workflowInScope1);
        Assert.NotNull(workflowInScope2);

        // 3. Scope 1 updates workflow details and saves successfully
        workflowInScope1.UpdateDetails("Workflow Renamed by Scope 1", "New Description 1", _clock);
        await db1.SaveChangesAsync();

        // 4. Scope 2 updates workflow details and attempts to save with stale xmin token
        workflowInScope2.UpdateDetails("Workflow Renamed by Scope 2", "New Description 2", _clock);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }
    [Fact]
    public async Task GivenWorkflowDeletionAfterConcurrentEdit_WhenStaleDeleteSaves_ThrowsDbUpdateConcurrencyException()
    {
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed workflow
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync();
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            await db.SaveChangesAsync();
        }

        // 2. Open two separate scopes and load the same workflow in both
        using var scopeDelete = _fixture.CreateScope();
        using var scopeEdit = _fixture.CreateScope();

        scopeDelete.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);
        scopeEdit.ServiceProvider.GetRequiredService<WorkspaceContext>().SetContext(workspace.Id, user.Id);

        var dbDelete = scopeDelete.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbEdit = scopeEdit.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var workflowToDelete = await dbDelete.Workflows.FindAsync(workflow.Id);
        var workflowToEdit = await dbEdit.Workflows.FindAsync(workflow.Id);

        Assert.NotNull(workflowToDelete);
        Assert.NotNull(workflowToEdit);

        // 3. Edit scope updates the workflow details and saves (advancing xmin in PostgreSQL)
        workflowToEdit.UpdateDetails("Updated Name Before Delete", "Updated Description", _clock);
        await dbEdit.SaveChangesAsync();

        // 4. Delete scope attempts to delete using stale xmin token
        dbDelete.Workflows.Remove(workflowToDelete);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbDelete.SaveChangesAsync());
    }
    [Fact]
    public async Task GivenNormalSequentialWorkflowLifecycle_WhenConcurrencyEnabled_AllOperationsSucceed()
    {
        var workflowId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Create and seed workflow and initial draft
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync(workflowId: workflowId);
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var draft = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition("step-1"),
                _validator,
                _clock);

            db.WorkflowVersions.Add(draft);
            await db.SaveChangesAsync();
        }

        // 2. Sequential edit in a new scope
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draft = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(draft);

            draft.UpdateDraft(CreateSampleDefinition("step-1-updated"), _validator, _clock);
            await db.SaveChangesAsync();
        }

        // 3. Sequential publish in a new scope
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draft = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(draft);

            draft.Publish(_validator, _clock);
            await db.SaveChangesAsync();
        }

        // 4. Update workflow with active version and activate in a new scope
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wf = await db.Workflows.FindAsync(workflowId);
            var version = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(wf);
            Assert.NotNull(version);

            wf.ActivateVersion(version, _clock);
            wf.Activate(_clock);
            await db.SaveChangesAsync();
        }

        // 5. Delete workflow with cascade in a new scope
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wf = await db.Workflows.FindAsync(workflowId);
            Assert.NotNull(wf);

            db.Workflows.Remove(wf);
            await db.SaveChangesAsync();
        }

        // 6. Verify workflow and version are removed
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wf = await db.Workflows.FindAsync(workflowId);
            var v = await db.WorkflowVersions.FindAsync(versionId);

            Assert.Null(wf);
            Assert.Null(v);
        }
    }

}
