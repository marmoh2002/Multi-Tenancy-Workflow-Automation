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
using WorkflowAutomation.Workflows.Core.Enums;
using WorkflowAutomation.Workspaces.Core.Entities;
using Xunit;

public class WorkflowStepExecutionPersistenceTests : IClassFixture<PostgreSqlFixture>
{
    private const string PrimaryStepId = "step-1";
    private const string SecondaryStepId = "step-2";

    private readonly PostgreSqlFixture _fixture;
    private readonly TestClock _clock = new();
    private readonly KahnDagValidator _validator = new();

    public WorkflowStepExecutionPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record SeededExecution(
        Guid WorkspaceId,
        Guid UserId,
        Guid WorkflowId,
        Guid VersionId,
        Guid ExecutionId);

    /// <summary>
    /// Seeds a user, workspace, workflow, published version, and a running execution
    /// whose pinned definition contains the steps used by these tests.
    /// </summary>
    private async Task<SeededExecution> SeedExecutionAsync(string workflowName)
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var definition = new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition { Id = PrimaryStepId, Type = "Log" },
                new StepDefinition { Id = SecondaryStepId, Type = "Notify" }
            ]
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
        var workspace = Workspace.CreatePersonal(workspaceId, userId, "Step Execution Workspace");
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

        execution.Start(_clock);

        db.Users.Add(user);
        db.Workspaces.Add(workspace);
        db.Workflows.Add(workflow);
        db.WorkflowVersions.Add(version);
        db.WorkflowExecutions.Add(execution);
        await db.SaveChangesAsync();

        return new SeededExecution(workspaceId, userId, workflowId, versionId, executionId);
    }

    /// <summary>
    /// Creates a step execution through the parent aggregate, applies an optional
    /// lifecycle transition, and persists it.
    /// </summary>
    private async Task<WorkflowStepExecution> AddStepExecutionAsync(
        SeededExecution seeded,
        string stepId = PrimaryStepId,
        int attemptNumber = 1,
        Action<WorkflowStepExecution>? transition = null)
    {
        using var scope = _fixture.CreateScope();
        var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        workspaceContext.SetContext(seeded.WorkspaceId, seeded.UserId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var execution = await db.WorkflowExecutions
            .SingleAsync(e => e.Id == seeded.ExecutionId);

        var stepExecution = execution.CreateStepExecution(
            Guid.NewGuid(),
            stepId,
            attemptNumber,
            _clock);

        transition?.Invoke(stepExecution);

        db.WorkflowStepExecutions.Add(stepExecution);
        await db.SaveChangesAsync();

        return stepExecution;
    }

    /// <summary>
    /// Reloads a step execution from PostgreSQL in a fresh, tenant-scoped DbContext.
    /// </summary>
    private async Task<WorkflowStepExecution> ReloadStepExecutionAsync(
        SeededExecution seeded,
        Guid stepExecutionId)
    {
        using var scope = _fixture.CreateScope();
        var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        workspaceContext.SetContext(seeded.WorkspaceId, seeded.UserId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.WorkflowStepExecutions
            .AsNoTracking()
            .SingleAsync(s => s.Id == stepExecutionId);
    }

    [Fact]
    public async Task GivenPendingStep_WhenSavedAndReloaded_RoundTripsAllProperties()
    {
        // 1. Arrange: a freshly created step execution in Pending state
        var seeded = await SeedExecutionAsync("Pending Step Workflow");
        var step = await AddStepExecutionAsync(seeded);

        // 2. Act: reload the row from PostgreSQL
        var reloaded = await ReloadStepExecutionAsync(seeded, step.Id);

        // 3. Assert: identity, ownership, and every nullable field round-trip
        Assert.Equal(step.Id, reloaded.Id);
        Assert.Equal(seeded.WorkspaceId, reloaded.WorkspaceId);
        Assert.Equal(seeded.ExecutionId, reloaded.ExecutionId);
        Assert.Equal(PrimaryStepId, reloaded.StepId);
        Assert.Equal(1, reloaded.AttemptNumber);
        Assert.Equal(WorkflowStepExecutionStatus.Pending, reloaded.Status);
        Assert.Null(reloaded.StartedAt);
        Assert.Null(reloaded.CompletedAt);
        Assert.Null(reloaded.CancelledAt);
        Assert.Null(reloaded.FinishedAt);
        Assert.Null(reloaded.ErrorMessage);
        Assert.False(reloaded.IsRetryable);
        Assert.Null(reloaded.OutputMetadata);
        Assert.Equal(step.CreatedAt, reloaded.CreatedAt);
        Assert.Null(reloaded.UpdatedAt);
    }

    [Fact]
    public async Task GivenCompletedStepWithMetadata_WhenSavedAndReloaded_RoundTripsSuccessfully()
    {
        // 1. Arrange: a step that starts and completes with output metadata
        var seeded = await SeedExecutionAsync("Completed Step Workflow");
        const string metadata = """{"processed":100,"target":"customers"}""";

        _clock.Advance(TimeSpan.FromMinutes(1));
        var step = await AddStepExecutionAsync(seeded, transition: s =>
        {
            s.Start(_clock);
            _clock.Advance(TimeSpan.FromMinutes(2));
            s.Complete(metadata, _clock);
        });

        // 2. Act
        var reloaded = await ReloadStepExecutionAsync(seeded, step.Id);

        // 3. Assert: status, timestamps, and metadata survive the round-trip
        Assert.Equal(WorkflowStepExecutionStatus.Completed, reloaded.Status);
        Assert.Equal(step.StartedAt, reloaded.StartedAt);
        Assert.Equal(step.CompletedAt, reloaded.CompletedAt);
        Assert.Equal(step.FinishedAt, reloaded.FinishedAt);
        Assert.NotNull(reloaded.StartedAt);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.Null(reloaded.CancelledAt);
        Assert.Equal(metadata, reloaded.OutputMetadata);
        Assert.Null(reloaded.ErrorMessage);
        Assert.False(reloaded.IsRetryable);
        Assert.Equal(step.CreatedAt, reloaded.CreatedAt);
        Assert.Equal(step.UpdatedAt, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task GivenFailedStepWithRetryable_WhenSavedAndReloaded_RoundTripsErrorMessageAndFlag()
    {
        // 1. Arrange: a step that fails with a retryable error (message is untrimmed on purpose)
        var seeded = await SeedExecutionAsync("Failed Step Workflow");

        _clock.Advance(TimeSpan.FromMinutes(1));
        var step = await AddStepExecutionAsync(seeded, transition: s =>
        {
            s.Start(_clock);
            _clock.Advance(TimeSpan.FromMinutes(2));
            s.Fail("  Downstream API timeout  ", isRetryable: true, _clock);
        });

        // 2. Act
        var reloaded = await ReloadStepExecutionAsync(seeded, step.Id);

        // 3. Assert: the trimmed error message and retryable flag round-trip
        Assert.Equal(WorkflowStepExecutionStatus.Failed, reloaded.Status);
        Assert.Equal("Downstream API timeout", reloaded.ErrorMessage);
        Assert.True(reloaded.IsRetryable);
        Assert.Equal(step.StartedAt, reloaded.StartedAt);
        Assert.Equal(step.CompletedAt, reloaded.CompletedAt);
        Assert.Equal(step.FinishedAt, reloaded.FinishedAt);
        Assert.Null(reloaded.CancelledAt);
        Assert.Null(reloaded.OutputMetadata);
        Assert.Equal(step.UpdatedAt, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task GivenCancelledStep_WhenSavedAndReloaded_RoundTripsTimestamps()
    {
        // 1. Arrange: a running step that is cancelled
        var seeded = await SeedExecutionAsync("Cancelled Step Workflow");

        _clock.Advance(TimeSpan.FromMinutes(1));
        var step = await AddStepExecutionAsync(seeded, transition: s =>
        {
            s.Start(_clock);
            _clock.Advance(TimeSpan.FromMinutes(2));
            s.Cancel(_clock);
        });

        // 2. Act
        var reloaded = await ReloadStepExecutionAsync(seeded, step.Id);

        // 3. Assert: StartedAt is preserved and CancelledAt becomes the finish time
        Assert.Equal(WorkflowStepExecutionStatus.Cancelled, reloaded.Status);
        Assert.Equal(step.StartedAt, reloaded.StartedAt);
        Assert.NotNull(reloaded.StartedAt);
        Assert.Null(reloaded.CompletedAt);
        Assert.Equal(step.CancelledAt, reloaded.CancelledAt);
        Assert.NotNull(reloaded.CancelledAt);
        Assert.Equal(step.CancelledAt, reloaded.FinishedAt);
        Assert.Null(reloaded.ErrorMessage);
        Assert.False(reloaded.IsRetryable);
        Assert.Null(reloaded.OutputMetadata);
    }

    [Fact]
    public async Task GivenSkippedStep_WhenSavedAndReloaded_RoundTripsTimestamps()
    {
        // 1. Arrange: a pending step that is skipped without ever starting
        var seeded = await SeedExecutionAsync("Skipped Step Workflow");

        _clock.Advance(TimeSpan.FromMinutes(1));
        var step = await AddStepExecutionAsync(seeded, transition: s =>
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            s.Skip(_clock);
        });

        // 2. Act
        var reloaded = await ReloadStepExecutionAsync(seeded, step.Id);

        // 3. Assert: CompletedAt is the finish time while StartedAt stays null
        Assert.Equal(WorkflowStepExecutionStatus.Skipped, reloaded.Status);
        Assert.Null(reloaded.StartedAt);
        Assert.Equal(step.CompletedAt, reloaded.CompletedAt);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.Null(reloaded.CancelledAt);
        Assert.Equal(step.CompletedAt, reloaded.FinishedAt);
        Assert.Null(reloaded.ErrorMessage);
        Assert.False(reloaded.IsRetryable);
        Assert.Null(reloaded.OutputMetadata);
        Assert.Equal(step.UpdatedAt, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task GivenDuplicateAttemptNumber_WhenSaved_ThrowsDbUpdateException()
    {
        // 1. Arrange: an existing attempt for step-1
        var seeded = await SeedExecutionAsync("Duplicate Attempt Workflow");
        var firstAttempt = await AddStepExecutionAsync(seeded, PrimaryStepId, attemptNumber: 1);

        // 2. Act: a second row with the same (ExecutionId, StepId, AttemptNumber)
        var duplicateId = Guid.NewGuid();

        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(seeded.WorkspaceId, seeded.UserId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var execution = await db.WorkflowExecutions
                .SingleAsync(e => e.Id == seeded.ExecutionId);

            var duplicate = execution.CreateStepExecution(
                duplicateId,
                PrimaryStepId,
                attemptNumber: 1,
                _clock);

            db.WorkflowStepExecutions.Add(duplicate);

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pgEx = Assert.IsType<PostgresException>(ex.InnerException);

            Assert.Equal("23505", pgEx.SqlState);
            Assert.Equal(
                "IX_WorkflowStepExecutions_ExecutionId_StepId_AttemptNumber",
                pgEx.ConstraintName);
        }

        // 3. Assert: the failed insert did not create a second attempt row
        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(seeded.WorkspaceId, seeded.UserId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var attempts = await db.WorkflowStepExecutions
                .Where(s => s.ExecutionId == seeded.ExecutionId && s.StepId == PrimaryStepId)
                .ToListAsync();

            var persisted = Assert.Single(attempts);
            Assert.Equal(firstAttempt.Id, persisted.Id);
            Assert.NotEqual(duplicateId, persisted.Id);
        }
    }

    [Fact]
    public async Task GivenStepExecutionsInDifferentWorkspaces_WhenQueried_GlobalQueryFilterIsolatesTenant()
    {
        // 1. Arrange: one step execution in each of two tenants
        var first = await SeedExecutionAsync("Workspace A Step Workflow");
        var second = await SeedExecutionAsync("Workspace B Step Workflow");

        var firstStep = await AddStepExecutionAsync(first);
        var secondStep = await AddStepExecutionAsync(second);

        // 2. Assert: Workspace A sees only its own step execution
        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(first.WorkspaceId, first.UserId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var visible = await db.WorkflowStepExecutions
                .Where(s => s.Id == firstStep.Id || s.Id == secondStep.Id)
                .ToListAsync();

            var persisted = Assert.Single(visible);
            Assert.Equal(firstStep.Id, persisted.Id);
            Assert.Equal(first.WorkspaceId, persisted.WorkspaceId);
            Assert.Equal(first.ExecutionId, persisted.ExecutionId);
        }

        // 3. Assert: Workspace B sees only its own step execution
        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(second.WorkspaceId, second.UserId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var visible = await db.WorkflowStepExecutions
                .Where(s => s.Id == firstStep.Id || s.Id == secondStep.Id)
                .ToListAsync();

            var persisted = Assert.Single(visible);
            Assert.Equal(secondStep.Id, persisted.Id);
            Assert.Equal(second.WorkspaceId, persisted.WorkspaceId);
            Assert.Equal(second.ExecutionId, persisted.ExecutionId);
        }

        // 4. Assert: no workspace context fails closed, while the rows still exist
        using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var visible = await db.WorkflowStepExecutions
                .Where(s => s.Id == firstStep.Id || s.Id == secondStep.Id)
                .ToListAsync();

            Assert.Empty(visible);

            var unfiltered = await db.WorkflowStepExecutions
                .IgnoreQueryFilters()
                .Where(s => s.Id == firstStep.Id || s.Id == secondStep.Id)
                .ToListAsync();

            Assert.Equal(2, unfiltered.Count);
        }
    }

    [Fact]
    public async Task GivenCrossWorkspaceWrite_WhenSaved_TenantWriteValidationInterceptorThrows()
    {
        // 1. Arrange: an execution owned by Workspace B
        var workspaceA = await SeedExecutionAsync("Workspace A Write Attempt");
        var workspaceB = await SeedExecutionAsync("Workspace B Owned Execution");

        // 2. Act: in Workspace A, create a step execution for Workspace B's execution
        using var scopeA = _fixture.CreateScope();
        var workspaceContextA = scopeA.ServiceProvider.GetRequiredService<WorkspaceContext>();
        workspaceContextA.SetContext(workspaceA.WorkspaceId, workspaceA.UserId);
        var dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var executionB = await dbA.WorkflowExecutions
            .IgnoreQueryFilters()
            .SingleAsync(e => e.Id == workspaceB.ExecutionId);

        var crossWorkspaceStep = executionB.CreateStepExecution(
            Guid.NewGuid(),
            PrimaryStepId,
            attemptNumber: 1,
            _clock);

        dbA.WorkflowStepExecutions.Add(crossWorkspaceStep);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => dbA.SaveChangesAsync());

        // 3. Assert: the interceptor identifies the tenant boundary and the entity type
        Assert.Contains("Cross-workspace write detected", ex.Message);
        Assert.Contains(nameof(WorkflowStepExecution), ex.Message);
        Assert.Contains(workspaceB.WorkspaceId.ToString(), ex.Message);
        Assert.Contains(workspaceA.WorkspaceId.ToString(), ex.Message);

        // 4. Assert: the rejected row was never persisted
        using var verifyScope = _fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var exists = await verifyDb.WorkflowStepExecutions
            .IgnoreQueryFilters()
            .AnyAsync(s => s.Id == crossWorkspaceStep.Id);

        Assert.False(exists, "Entity rejected by the interceptor must not be persisted.");
    }

    [Fact]
    public async Task GivenMismatchedWorkspaceId_WhenRawInserted_CompositeForeignKeyRejects()
    {
        // 1. Arrange: an execution in Workspace A and a separate Workspace B that exists
        var executionA = await SeedExecutionAsync("Composite FK Execution Owner");
        var workspaceB = await SeedExecutionAsync("Composite FK Other Workspace");

        // 2. Act: insert a row referencing execution A but claiming workspace B.
        //    The single-column workspace FK passes; the composite FK must reject it.
        using var scope = _fixture.CreateScope();
        var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        workspaceContext.SetContext(workspaceB.WorkspaceId, workspaceB.UserId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var insertedId = Guid.NewGuid();

        const string sql = """
            INSERT INTO "WorkflowStepExecutions" (
                "Id", "WorkspaceId", "ExecutionId", "StepId", "AttemptNumber", "Status",
                "StartedAt", "CompletedAt", "CancelledAt", "ErrorMessage", "IsRetryable",
                "OutputMetadata", "CreatedAt", "UpdatedAt")
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, NULL, NULL, NULL, NULL, FALSE, NULL, {6}, NULL)
            """;

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                sql,
                insertedId,
                workspaceB.WorkspaceId,
                executionA.ExecutionId,
                PrimaryStepId,
                1,
                1,
                _clock.UtcNow));

        // 3. Assert: PostgreSQL reports the composite FK, not the workspace FK
        Assert.Equal("23503", ex.SqlState);
        Assert.Equal(
            "FK_WorkflowStepExecutions_Executions_WorkspaceId_ExecutionId",
            ex.ConstraintName);

        // 4. Assert: the rejected row was never persisted
        using var verifyScope = _fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(await verifyDb.WorkflowStepExecutions
            .IgnoreQueryFilters()
            .AnyAsync(s => s.Id == insertedId));
    }

    [Fact]
    public async Task GivenExecutionWithSteps_WhenExecutionDeleted_CascadesStepAttempts()
    {
        // 1. Arrange: three attempts (including a retry) belonging to one execution
        var seeded = await SeedExecutionAsync("Cascade Step Workflow");

        var firstAttempt = await AddStepExecutionAsync(seeded, PrimaryStepId, attemptNumber: 1);
        var secondAttempt = await AddStepExecutionAsync(seeded, PrimaryStepId, attemptNumber: 2);
        var otherStepAttempt = await AddStepExecutionAsync(seeded, SecondaryStepId, attemptNumber: 1);

        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(seeded.WorkspaceId, seeded.UserId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.Equal(3, await db.WorkflowStepExecutions
                .CountAsync(s => s.ExecutionId == seeded.ExecutionId));
        }

        // 2. Act: delete the parent execution; the database cascade removes attempts
        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(seeded.WorkspaceId, seeded.UserId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var execution = await db.WorkflowExecutions
                .SingleAsync(e => e.Id == seeded.ExecutionId);

            db.WorkflowExecutions.Remove(execution);
            await db.SaveChangesAsync();
        }

        // 3. Assert: the execution and all step attempts are gone
        using (var scope = _fixture.CreateScope())
        {
            var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            workspaceContext.SetContext(seeded.WorkspaceId, seeded.UserId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.False(await db.WorkflowExecutions
                .AnyAsync(e => e.Id == seeded.ExecutionId));

            var survivingAttempts = await db.WorkflowStepExecutions
                .IgnoreQueryFilters()
                .Where(s =>
                    s.Id == firstAttempt.Id ||
                    s.Id == secondAttempt.Id ||
                    s.Id == otherStepAttempt.Id)
                .ToListAsync();

            Assert.Empty(survivingAttempts);

            // The workflow and its version are not part of the cascade
            Assert.True(await db.Workflows.AnyAsync(w => w.Id == seeded.WorkflowId));
            Assert.True(await db.WorkflowVersions.AnyAsync(v => v.Id == seeded.VersionId));
        }
    }

    /// <summary>
    /// Deterministic clock whose values align with PostgreSQL's microsecond
    /// precision, so timestamp round-trip assertions are exact.
    /// </summary>
    private sealed class TestClock : IClock
    {
        private DateTime _utcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public DateTime UtcNow => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
