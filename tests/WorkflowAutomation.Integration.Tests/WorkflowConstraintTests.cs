namespace WorkflowAutomation.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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


public class WorkflowConstraintTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly SystemClock _clock = new();
    private readonly KahnDagValidator _validator = new();


    public WorkflowConstraintTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private static WorkflowDefinition CreateSampleDefinition(string stepId = "step-1")
    {
        return new WorkflowDefinition
        {
            Steps = [new StepDefinition { Id = stepId, Type = "Log" }]
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
        var workflow = Workflow.Create(wfId, wsId, uId, "Valid Workflow", "Description", _clock);

        return (user, workspace, workflow);
    }

    [Fact]
    public async Task GivenWorkflowWithUntrimmedName_WhenSaved_ThrowsCheckConstraintViolation()
    {
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed valid workflow
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

        // 2. Load workflow in a new scope, bypass domain guard to set untrimmed name, and save
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wf = await db.Workflows.FindAsync(workflow.Id);
            Assert.NotNull(wf);

            db.Entry(wf).Property(w => w.Name).CurrentValue = "  Untrimmed Name  ";

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal("CK_Workflows_Name_TrimmedAndNotEmpty", pgEx.ConstraintName);
        }
    }
    [Fact]
    public async Task GivenWorkflowWithEmptyCreatedByUserId_WhenSaved_ThrowsCheckConstraintViolation()
    {
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed valid workflow
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

        // 2. Load workflow in a new scope, set CreatedByUserId to Guid.Empty, and save
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wf = await db.Workflows.FindAsync(workflow.Id);
            Assert.NotNull(wf);

            db.Entry(wf).Property(w => w.CreatedByUserId).CurrentValue = Guid.Empty;

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal("CK_Workflows_CreatedByUserId_NotEmpty", pgEx.ConstraintName);
        }
    }

    [Fact]
    public async Task GivenActiveWorkflowWithoutActiveVersion_WhenSaved_ThrowsCheckConstraintViolation()
    {
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed valid workflow (starts inactive with ActiveVersionId = null)
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

        // 2. Load workflow in a new scope, set IsActive = true without ActiveVersionId, and save
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            wsContext.SetContext(workspace.Id, user.Id);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wf = await db.Workflows.FindAsync(workflow.Id);
            Assert.NotNull(wf);

            db.Entry(wf).Property(w => w.IsActive).CurrentValue = true;

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal("CK_Workflows_IsActive_RequiresActiveVersionId", pgEx.ConstraintName);
        }
    }
    [Fact]
    public async Task GivenWorkflowVersionWithZeroVersionNumber_WhenSaved_ThrowsCheckConstraintViolation()
    {
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

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
                Guid.NewGuid(),
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition(),
                _validator,
                _clock);

            db.WorkflowVersions.Add(draft);
            db.Entry(draft).Property(v => v.VersionNumber).CurrentValue = 0;

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal("CK_WorkflowVersions_VersionNumber_Positive", pgEx.ConstraintName);
        }
    }

    [Fact]
    public async Task GivenWorkflowVersionWithZeroSchemaVersion_WhenSaved_ThrowsCheckConstraintViolation()
    {
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

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
                Guid.NewGuid(),
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition(),
                _validator,
                _clock);

            db.WorkflowVersions.Add(draft);
            db.Entry(draft).Property(v => v.DefinitionSchemaVersion).CurrentValue = 0;

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal("CK_WorkflowVersions_DefinitionSchemaVersion_Positive", pgEx.ConstraintName);
        }
    }
    [Fact]
    public async Task GivenWorkflowVersionWithArrayDefinition_WhenSaved_ThrowsCheckConstraintViolation()
    {
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

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
                Guid.NewGuid(),
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition(),
                _validator,
                _clock);

            db.WorkflowVersions.Add(draft);
            // Bypass domain serialization to set a JSON array instead of an object
            db.Entry(draft).Property(v => v.SerializedDefinition).CurrentValue = "[]";

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal("CK_WorkflowVersions_SerializedDefinition_IsObject", pgEx.ConstraintName);
        }
    }

    [Fact]
    public async Task GivenWorkflowVersionWithObjectValidationErrors_WhenSaved_ThrowsCheckConstraintViolation()
    {
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

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
                Guid.NewGuid(),
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition(),
                _validator,
                _clock);

            db.WorkflowVersions.Add(draft);
            // Validation errors must be a JSON array or SQL NULL, not a JSON object
            db.Entry(draft).Property(v => v.ValidationErrorsJson).CurrentValue = "{\"error\": \"not-an-array\"}";

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal("CK_WorkflowVersions_ValidationErrorsJson_IsArrayOrNull", pgEx.ConstraintName);
        }
    }
    [Fact]
    public async Task GivenPublishedVersionWithNullPublishedAt_WhenSavedDirectly_ThrowsCheckConstraintViolation()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed valid published version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync();
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var version = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition(),
                _validator,
                _clock);

            version.Publish(_validator, _clock);
            db.WorkflowVersions.Add(version);
            await db.SaveChangesAsync();
        }

        // 2. Bypass in-process interceptors via raw SQL to test PostgreSQL check constraint directly
        using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"WorkflowVersions\" SET \"PublishedAt\" = NULL WHERE \"Id\" = {0}",
                    versionId));

            Assert.Equal("CK_WorkflowVersions_PublicationTimestamp_Consistency", ex.ConstraintName);
        }
    }

    [Fact]
    public async Task GivenPublishedVersionWithValidationErrors_WhenSavedDirectly_ThrowsCheckConstraintViolation()
    {
        var versionId = Guid.NewGuid();
        Workspace workspace;
        ApplicationUser user;
        Workflow workflow;

        // 1. Seed valid published version
        using (var scope = _fixture.CreateScope())
        {
            var wsContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            (user, workspace, workflow) = await SeedPrerequisitesAsync();
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);

            wsContext.SetContext(workspace.Id, user.Id);
            var version = WorkflowVersion.CreateDraft(
                versionId,
                workspace.Id,
                workflow.Id,
                versionNumber: 1,
                CreateSampleDefinition(),
                _validator,
                _clock);

            version.Publish(_validator, _clock);
            db.WorkflowVersions.Add(version);
            await db.SaveChangesAsync();
        }

        // 2. Bypass in-process interceptors via raw SQL to test PostgreSQL check constraint directly
        using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"WorkflowVersions\" SET \"ValidationErrorsJson\" = '[\"error\"]'::jsonb WHERE \"Id\" = {0}",
                    versionId));

            Assert.Equal("CK_WorkflowVersions_Published_NoValidationErrors", ex.ConstraintName);
        }
    }
    [Fact]
    public async Task GivenWorkflowWithActiveVersion_WhenWorkflowDeletedWithVersionsUnloaded_CascadeDeletesAllVersions()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var version1Id = Guid.NewGuid();
        var version2Id = Guid.NewGuid();

        // 1. Arrange: Persist a workflow with an active published version and an unpublished draft
        using (var scope = _fixture.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Active Cascade Workflow", null, _clock);

            var def1 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s1", Type = "Action1" }] };
            var def2 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s2", Type = "Action2" }] };
            var v1 = WorkflowVersion.CreateDraft(version1Id, workspaceId, workflowId, 1, def1, _validator, _clock);

            var v2 = WorkflowVersion.CreateDraft(version2Id, workspaceId, workflowId, 2, def2, _validator, _clock);

            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);
            db.WorkflowVersions.AddRange(v1, v2);

            await db.SaveChangesAsync();
            v1.Publish(_validator, _clock);
            workflow.ActivateVersion(v1, _clock);
            workflow.Activate(_clock);


            await db.SaveChangesAsync();
        }

        // 2. Act: In an isolated scope, load ONLY the workflow (versions remain unloaded / untracked)
        using (var deleteScope = _fixture.CreateScope())
        {
            var deleteCtx = deleteScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            deleteCtx.SetContext(workspaceId, userId);
            var deleteDb = deleteScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workflowToDelete = await deleteDb.Workflows.FindAsync(workflowId);
            Assert.NotNull(workflowToDelete);
            Assert.Equal(version1Id, workflowToDelete.ActiveVersionId);
            Assert.True(workflowToDelete.IsActive);

            deleteDb.Workflows.Remove(workflowToDelete);
            await deleteDb.SaveChangesAsync();
        }

        // 3. Assert: In a fresh scope, confirm workflow and all versions are deleted from PostgreSQL
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
    public async Task GivenWorkflowWithActiveVersion_WhenWorkflowDeletedWithVersionsLoaded_SucceedsAndCascadeDeletesAllVersions()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var version1Id = Guid.NewGuid();
        var version2Id = Guid.NewGuid();

        // 1. Arrange: Persist workflow and versions, then activate v1
        using (var scope = _fixture.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Loaded Active Workflow", null, _clock);

            var def1 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s1", Type = "Action1" }] };
            var def2 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s2", Type = "Action2" }] };
            var v1 = WorkflowVersion.CreateDraft(version1Id, workspaceId, workflowId, 1, def1, _validator, _clock);
            var v2 = WorkflowVersion.CreateDraft(version2Id, workspaceId, workflowId, 2, def2, _validator, _clock);

            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);
            db.WorkflowVersions.AddRange(v1, v2);
            await db.SaveChangesAsync();

            v1.Publish(_validator, _clock);
            workflow.ActivateVersion(v1, _clock);
            workflow.Activate(_clock);
            await db.SaveChangesAsync();
        }

        // 2. Act: In an isolated scope, load workflow AND its versions into ChangeTracker, then delete workflow
        using (var deleteScope = _fixture.CreateScope())
        {
            var deleteCtx = deleteScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            deleteCtx.SetContext(workspaceId, userId);
            var deleteDb = deleteScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workflowToDelete = await deleteDb.Workflows.FindAsync(workflowId);
            var v1 = await deleteDb.WorkflowVersions.FindAsync(version1Id);
            var v2 = await deleteDb.WorkflowVersions.FindAsync(version2Id);

            Assert.NotNull(workflowToDelete);
            Assert.NotNull(v1);
            Assert.NotNull(v2);
            await using var tx = await deleteDb.Database.BeginTransactionAsync();
            workflowToDelete.PrepareForDeletion(_clock);
            await deleteDb.SaveChangesAsync();

            deleteDb.Workflows.Remove(workflowToDelete);
            await deleteDb.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // 3. Assert: In a fresh scope, confirm workflow and all versions are gone
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
    public async Task GivenWorkflowWithActiveVersion_WhenWorkflowDeletedWithVersionsPartiallyLoaded_SucceedsAndCascadeDeletesAll()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var version1Id = Guid.NewGuid();
        var version2Id = Guid.NewGuid();

        // 1. Arrange: Persist workflow and versions, then activate v1
        using (var scope = _fixture.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            ctx.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Partial Active Workflow", null, _clock);

            var def1 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s1", Type = "Action1" }] };
            var def2 = new WorkflowDefinition { Steps = [new StepDefinition { Id = "s2", Type = "Action2" }] };
            var v1 = WorkflowVersion.CreateDraft(version1Id, workspaceId, workflowId, 1, def1, _validator, _clock);
            var v2 = WorkflowVersion.CreateDraft(version2Id, workspaceId, workflowId, 2, def2, _validator, _clock);

            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);
            db.WorkflowVersions.AddRange(v1, v2);
            await db.SaveChangesAsync();

            v1.Publish(_validator, _clock);
            workflow.ActivateVersion(v1, _clock);
            workflow.Activate(_clock);
            await db.SaveChangesAsync();
        }

        // 2. Act: In an isolated scope, load workflow and ONLY ONE child version (v1), leaving v2 unloaded
        using (var deleteScope = _fixture.CreateScope())
        {
            var deleteCtx = deleteScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            deleteCtx.SetContext(workspaceId, userId);
            var deleteDb = deleteScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workflowToDelete = await deleteDb.Workflows.FindAsync(workflowId);
            var v1 = await deleteDb.WorkflowVersions.FindAsync(version1Id);
            // v2 is NOT loaded into ChangeTracker

            Assert.NotNull(workflowToDelete);
            Assert.NotNull(v1);
            await using var tx = await deleteDb.Database.BeginTransactionAsync();
            workflowToDelete.PrepareForDeletion(_clock);

            await deleteDb.SaveChangesAsync();


            deleteDb.Workflows.Remove(workflowToDelete);
            await deleteDb.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // 3. Assert: In a fresh scope, confirm workflow and all versions are gone
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

}
