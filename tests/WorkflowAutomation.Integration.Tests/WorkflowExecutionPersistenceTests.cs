namespace WorkflowAutomation.Integration.Tests;

using System.Security.Cryptography;
using System.Text;

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
using WorkflowAutomation.Workflows.Core.Enums;
using WorkflowAutomation.Workspaces.Core.Entities;
using Xunit;

public class WorkflowExecutionPersistenceTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly SystemClock _clock = new();
    private readonly KahnDagValidator _validator = new();

    public WorkflowExecutionPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record SeededExecution(
        Guid WorkspaceId,
        Guid UserId,
        Guid WorkflowId,
        Guid VersionId,
        Guid ExecutionId,
        string WorkflowName,
        int VersionNumber,
        int DefinitionSchemaVersion,
        string DefinitionSnapshot,
        string DefinitionHash);

    private async Task<SeededExecution> SeedExecutionAsync(
        string workflowName = "Snapshot Workflow",
        WorkflowExecutionStatus? terminalStatus = null)
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var definition = new WorkflowDefinition
        {
            Steps = [new StepDefinition { Id = "step-1", Type = "Log" }]
        };

        using var scope = _fixture.CreateScope();
        var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        workspaceContext.SetContext(workspaceId, userId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"user_{userId:N}",
            Email = $"{userId:N}@test.com"
        };
        var workspace = Workspace.CreatePersonal(workspaceId, userId, "Execution Workspace");
        var workflow = Workflow.Create(workflowId, workspaceId, userId, workflowName, null, _clock);
        var version = WorkflowVersion.CreateDraft(
            versionId,
            workspaceId,
            workflowId,
            1,
            definition,
            _validator,
            _clock);

        version.Publish(_validator, _clock);

        var execution = WorkflowExecution.Create(
            executionId,
            workflow,
            version,
            WorkflowExecutionSource.Manual,
            _clock,
            requestedByUserId: userId);

        if (terminalStatus == WorkflowExecutionStatus.Completed)
        {
            execution.Start(_clock);
            execution.Complete(_clock);
        }
        else if (terminalStatus == WorkflowExecutionStatus.Failed)
        {
            execution.Start(_clock);
            execution.Fail("step-1", "Step failed.", _clock);
        }
        else if (terminalStatus == WorkflowExecutionStatus.Cancelled)
        {
            execution.RequestCancellation("Cancelled by test.", _clock);
            execution.MarkCancelled(_clock);
        }

        db.Users.Add(user);
        db.Workspaces.Add(workspace);
        db.Workflows.Add(workflow);
        db.WorkflowVersions.Add(version);
        db.WorkflowExecutions.Add(execution);
        await db.SaveChangesAsync();

        return new SeededExecution(
            workspaceId,
            userId,
            workflowId,
            versionId,
            executionId,
            execution.WorkflowName,
            execution.VersionNumber,
            execution.DefinitionSchemaVersion,
            execution.DefinitionSnapshot,
            execution.DefinitionHash);
    }

    [Fact]
    public async Task GivenExecution_WhenSavedAndReloaded_SnapshotFieldsRoundTripAndHashIsIntact()
    {
        // 1. Arrange: persist a terminal execution with its immutable snapshot
        var seeded = await SeedExecutionAsync(
            "Round Trip Workflow",
            WorkflowExecutionStatus.Completed);

        // 2. Act: reload the execution from PostgreSQL in a fresh scope
        using var scope = _fixture.CreateScope();
        var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        workspaceContext.SetContext(seeded.WorkspaceId, seeded.UserId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var execution = await db.WorkflowExecutions
            .SingleAsync(e => e.Id == seeded.ExecutionId);

        // 3. Assert: every pinned snapshot field round-trips byte-for-byte
        Assert.Equal(seeded.WorkflowId, execution.WorkflowId);
        Assert.Equal(seeded.VersionId, execution.WorkflowVersionId);
        Assert.Equal(seeded.WorkflowName, execution.WorkflowName);
        Assert.Equal(seeded.VersionNumber, execution.VersionNumber);
        Assert.Equal(seeded.DefinitionSchemaVersion, execution.DefinitionSchemaVersion);
        Assert.Equal(seeded.DefinitionSnapshot, execution.DefinitionSnapshot);
        Assert.Equal(seeded.DefinitionHash, execution.DefinitionHash);

        // The hash is the lowercase SHA-256 digest of the exact stored text
        var expectedHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(seeded.DefinitionSnapshot)));
        Assert.Equal(expectedHash, execution.DefinitionHash);
        Assert.True(execution.HasIntactSnapshot());

        // The snapshot still deserializes into a usable definition after reload
        var definition = execution.GetDefinition();
        var step = Assert.Single(definition.Steps);
        Assert.Equal("step-1", step.Id);
        Assert.Equal("Log", step.Type);
        Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status);
    }

    [Fact]
    public async Task GivenExecutionsInTwoWorkspaces_WhenQuerying_ReturnsOnlyActiveWorkspaceExecutions()
    {
        // 1. Arrange: one execution in each of two tenants
        var first = await SeedExecutionAsync("Workspace A Execution");
        var second = await SeedExecutionAsync("Workspace B Execution");

        // 2. Act & Assert: Workspace A sees only its own execution
        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(first.WorkspaceId, first.UserId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var visible = await db.WorkflowExecutions
                .Where(e => e.Id == first.ExecutionId || e.Id == second.ExecutionId)
                .ToListAsync();

            var execution = Assert.Single(visible);
            Assert.Equal(first.ExecutionId, execution.Id);
            Assert.Equal(first.WorkspaceId, execution.WorkspaceId);
        }

        // 3. Act & Assert: Workspace B sees only its own execution
        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(second.WorkspaceId, second.UserId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var visible = await db.WorkflowExecutions
                .Where(e => e.Id == first.ExecutionId || e.Id == second.ExecutionId)
                .ToListAsync();

            var execution = Assert.Single(visible);
            Assert.Equal(second.ExecutionId, execution.Id);
            Assert.Equal(second.WorkspaceId, execution.WorkspaceId);
        }

        // 4. Act & Assert: no workspace context is fail-closed, while the rows still exist
        using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var visible = await db.WorkflowExecutions
                .Where(e => e.Id == first.ExecutionId || e.Id == second.ExecutionId)
                .ToListAsync();

            Assert.Empty(visible);

            var unfiltered = await db.WorkflowExecutions
                .IgnoreQueryFilters()
                .Where(e => e.Id == first.ExecutionId || e.Id == second.ExecutionId)
                .ToListAsync();

            Assert.Equal(2, unfiltered.Count);
        }
    }

    [Fact]
    public async Task GivenWorkflowWithTerminalExecutions_WhenWorkflowDeleted_VersionsAreRemovedAndHistoryIsRetained()
    {
        // 1. Arrange: one active workflow with two published versions and three terminal executions
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var version1Id = Guid.NewGuid();
        var version2Id = Guid.NewGuid();
        var completedExecutionId = Guid.NewGuid();
        var failedExecutionId = Guid.NewGuid();
        var cancelledExecutionId = Guid.NewGuid();
        var completedStepId = Guid.NewGuid();
        var failedStepId = Guid.NewGuid();
        var cancelledStepId = Guid.NewGuid();
        string expectedCompletedSnapshot;

        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser
            {
                Id = userId,
                UserName = $"user_{userId:N}",
                Email = $"{userId:N}@test.com"
            };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Retention Workspace");
            var workflow = Workflow.Create(
                workflowId,
                workspaceId,
                userId,
                "Retained History Workflow",
                null,
                _clock);

            var definition1 = new WorkflowDefinition
            {
                Steps = [new StepDefinition { Id = "step-1", Type = "Log" }]
            };
            var definition2 = new WorkflowDefinition
            {
                Steps =
                [
                    new StepDefinition { Id = "step-1", Type = "Log" },
                    new StepDefinition
                    {
                        Id = "step-2",
                        Type = "Notify",
                        DependsOn = ["step-1"]
                    }
                ]
            };

            var version1 = WorkflowVersion.CreateDraft(
                version1Id,
                workspaceId,
                workflowId,
                1,
                definition1,
                _validator,
                _clock);
            var version2 = WorkflowVersion.CreateDraft(
                version2Id,
                workspaceId,
                workflowId,
                2,
                definition2,
                _validator,
                _clock);

            version1.Publish(_validator, _clock);
            version2.Publish(_validator, _clock);

            var completed = WorkflowExecution.Create(
                completedExecutionId,
                workflow,
                version1,
                WorkflowExecutionSource.Manual,
                _clock,
                requestedByUserId: userId);
            completed.Start(_clock);

            // Step attempts can only be created while the parent is Pending or Running,
            // so record them before each execution reaches its terminal state.
            var completedStep = completed.CreateStepExecution(
                completedStepId,
                "step-1",
                attemptNumber: 1,
                _clock);
            completedStep.Start(_clock);
            completedStep.Complete("""{"logged":true}""", _clock);
            completed.Complete(_clock);

            var failed = WorkflowExecution.Create(
                failedExecutionId,
                workflow,
                version1,
                WorkflowExecutionSource.Manual,
                _clock,
                requestedByUserId: userId);
            failed.Start(_clock);

            var failedStep = failed.CreateStepExecution(
                failedStepId,
                "step-1",
                attemptNumber: 1,
                _clock);
            failedStep.Start(_clock);
            failedStep.Fail("Downstream timeout.", isRetryable: true, _clock);
            failed.Fail("step-1", "Step failed.", _clock);

            var cancelled = WorkflowExecution.Create(
                cancelledExecutionId,
                workflow,
                version2,
                WorkflowExecutionSource.Manual,
                _clock,
                requestedByUserId: userId);
            cancelled.Start(_clock);

            var cancelledStep = cancelled.CreateStepExecution(
                cancelledStepId,
                "step-1",
                attemptNumber: 1,
                _clock);
            cancelledStep.Start(_clock);
            cancelledStep.Cancel(_clock);
            cancelled.RequestCancellation("Cancelled by test.", _clock);
            cancelled.MarkCancelled(_clock);

            expectedCompletedSnapshot = completed.DefinitionSnapshot;

            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.Workflows.Add(workflow);
            db.WorkflowVersions.AddRange(version1, version2);
            db.WorkflowExecutions.AddRange(completed, failed, cancelled);
            await db.SaveChangesAsync();

            // Persist the attempts after their parent executions exist.
            db.WorkflowStepExecutions.AddRange(completedStep, failedStep, cancelledStep);
            await db.SaveChangesAsync();

            workflow.ActivateVersion(version1, _clock);
            workflow.Activate(_clock);
            await db.SaveChangesAsync();
        }

        // 2. Act: delete the workflow (its versions cascade, execution history has no FK)
        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var workflow = await db.Workflows.SingleAsync(w => w.Id == workflowId);
            await using var transaction = await db.Database.BeginTransactionAsync();

            workflow.PrepareForDeletion(_clock);
            await db.SaveChangesAsync();

            db.Workflows.Remove(workflow);
            await db.SaveChangesAsync();

            await transaction.CommitAsync();
        }

        // 3. Assert: workflow and all versions are gone, but every terminal execution survives
        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.Null(await db.Workflows.SingleOrDefaultAsync(w => w.Id == workflowId));
            Assert.Empty(await db.WorkflowVersions
                .Where(v => v.WorkflowId == workflowId)
                .ToListAsync());

            var executions = await db.WorkflowExecutions
                .Where(e => e.WorkflowId == workflowId)
                .ToListAsync();

            Assert.Equal(3, executions.Count);
            Assert.Contains(executions, e =>
                e.Id == completedExecutionId &&
                e.Status == WorkflowExecutionStatus.Completed);
            Assert.Contains(executions, e =>
                e.Id == failedExecutionId &&
                e.Status == WorkflowExecutionStatus.Failed);
            Assert.Contains(executions, e =>
                e.Id == cancelledExecutionId &&
                e.Status == WorkflowExecutionStatus.Cancelled);

            Assert.All(executions, execution =>
            {
                Assert.Equal("Retained History Workflow", execution.WorkflowName);
                Assert.True(execution.HasIntactSnapshot());
                Assert.NotNull(execution.GetDefinition());
            });

            var persistedCompleted = executions.Single(e => e.Id == completedExecutionId);
            Assert.Equal(expectedCompletedSnapshot, persistedCompleted.DefinitionSnapshot);

            // Step attempts are children of the retained executions, not of the workflow,
            // so they survive the workflow deletion alongside their parent executions.
            var stepExecutions = await db.WorkflowStepExecutions
                .Where(s =>
                    s.Id == completedStepId ||
                    s.Id == failedStepId ||
                    s.Id == cancelledStepId)
                .ToListAsync();

            Assert.Equal(3, stepExecutions.Count);

            var persistedCompletedStep = Assert.Single(
                stepExecutions,
                s => s.ExecutionId == completedExecutionId);
            Assert.Equal(completedStepId, persistedCompletedStep.Id);
            Assert.Equal(WorkflowStepExecutionStatus.Completed, persistedCompletedStep.Status);

            var persistedFailedStep = Assert.Single(
                stepExecutions,
                s => s.ExecutionId == failedExecutionId);
            Assert.Equal(failedStepId, persistedFailedStep.Id);
            Assert.Equal(WorkflowStepExecutionStatus.Failed, persistedFailedStep.Status);
            Assert.True(persistedFailedStep.IsRetryable);

            var persistedCancelledStep = Assert.Single(
                stepExecutions,
                s => s.ExecutionId == cancelledExecutionId);
            Assert.Equal(cancelledStepId, persistedCancelledStep.Id);
            Assert.Equal(WorkflowStepExecutionStatus.Cancelled, persistedCancelledStep.Status);
        }
    }
}
