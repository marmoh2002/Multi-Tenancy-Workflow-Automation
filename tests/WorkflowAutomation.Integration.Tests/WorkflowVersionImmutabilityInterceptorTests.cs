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

public class WorkflowVersionImmutabilityInterceptorTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly SystemClock _clock = new();
    private readonly KahnDagValidator _validator = new();

    public WorkflowVersionImmutabilityInterceptorTests(PostgreSqlFixture fixture)
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
        ApplicationDbContext db,
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
    public async Task GivenValidDraft_WhenDraftUpdatedAndSaved_SucceedsAndPersistsChanges()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed prerequisites and draft version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync(db);
            await db.SaveChangesAsync();
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

        // 2. Load draft in separate scope, update definition, and save
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draft = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(draft);

            draft.UpdateDraft(CreateSampleDefinition("step-updated"), _validator, _clock);
            await db.SaveChangesAsync();
        }

        // 3. Verify in separate scope
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draft = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(draft);

            var definition = draft.GetDefinition();
            Assert.Single(definition.Steps);
            Assert.Equal("step-updated", definition.Steps[0].Id);
        }
    }
    [Fact]
    public async Task GivenDraft_WhenPublishedAndSaved_SucceedsAndPersistsPublication()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed prerequisites and draft version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync(db);
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

        // 2. Load draft in separate scope, publish it, and save
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

        // 3. Verify in separate scope
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var version = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(version);

            Assert.True(version.IsPublished);
            Assert.NotNull(version.PublishedAt);
        }
    }
    [Fact]
    public async Task GivenPublishedVersion_WhenModified_ThrowsInvalidOperationException()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed prerequisites and published version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync(db);
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var version = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition("step-1"),
                _validator,
                _clock);

            version.Publish(_validator, _clock);

            db.WorkflowVersions.Add(version);
            await db.SaveChangesAsync();
        }

        // 2. In a new scope, load the published version, bypass domain setters via EF Entry, and try to save
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var publishedVersion = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(publishedVersion);

            // Directly mutate a property via EF Core ChangeTracker
            db.Entry(publishedVersion).Property(v => v.SerializedDefinition).CurrentValue = "{\"steps\":[]}";

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("Cannot modify or unpublish an already published workflow version", ex.Message);
        }
    }
    [Fact]
    public async Task GivenDraft_WhenImmutablePropertyModified_ThrowsInvalidOperationException()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed prerequisites and draft version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync(db);
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

        // 2. Load draft in a new scope, mutate VersionNumber via EF Core entry, and attempt to save
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draft = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(draft);

            db.Entry(draft).Property(v => v.VersionNumber).CurrentValue = 2;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("Cannot modify immutable property 'VersionNumber' on workflow version", ex.Message);
        }
    }
    [Fact]
    public async Task GivenDraft_WhenDeletedIndependently_Succeeds()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed prerequisites and draft version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync(db);
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

        // 2. Load draft in a new scope, delete it, and save
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draft = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(draft);

            db.WorkflowVersions.Remove(draft);
            await db.SaveChangesAsync();
        }

        // 3. Verify in a separate scope that it no longer exists
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draft = await db.WorkflowVersions.FindAsync(versionId);
            Assert.Null(draft);
        }
    }
    [Fact]
    public async Task GivenPublishedVersion_WhenDeletedIndependently_ThrowsInvalidOperationException()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed prerequisites and published version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync(db);
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var version = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition("step-1"),
                _validator,
                _clock);

            version.Publish(_validator, _clock);

            db.WorkflowVersions.Add(version);
            await db.SaveChangesAsync();
        }

        // 2. Load published version in a new scope and attempt to delete it independently
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var publishedVersion = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(publishedVersion);

            db.WorkflowVersions.Remove(publishedVersion);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("Cannot delete a published workflow version independently", ex.Message);
        }
    }

    [Fact]
    public async Task GivenPublishedVersion_WhenDeletedAlongWithOwningWorkflow_Succeeds()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed prerequisites and published version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync(db);
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var version = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition("step-1"),
                _validator,
                _clock);

            version.Publish(_validator, _clock);

            db.WorkflowVersions.Add(version);
            await db.SaveChangesAsync();
        }

        // 2. Load both workflow and published version in a new scope, delete both, and save
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wf = await db.Workflows.FindAsync(workflow.Id);
            var publishedVersion = await db.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(wf);
            Assert.NotNull(publishedVersion);

            db.Workflows.Remove(wf);
            db.WorkflowVersions.Remove(publishedVersion);

            await db.SaveChangesAsync();
        }

        // 3. Verify in a separate scope that both are deleted
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wf = await db.Workflows.FindAsync(workflow.Id);
            var publishedVersion = await db.WorkflowVersions.FindAsync(versionId);

            Assert.Null(wf);
            Assert.Null(publishedVersion);
        }
    }

}