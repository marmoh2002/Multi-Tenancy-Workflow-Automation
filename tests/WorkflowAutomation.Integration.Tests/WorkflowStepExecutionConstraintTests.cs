namespace WorkflowAutomation.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Infrastructure.Persistence;
using WorkflowAutomation.Integration.Tests.Fixtures;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Shared.Kernel.Time;
using WorkflowAutomation.Workflows.Core.Enums;
using WorkflowAutomation.Workspaces.Core.Entities;
using Xunit;

/// <summary>
/// Exercises every PostgreSQL check constraint on WorkflowStepExecutions with raw SQL
/// inserts that bypass EF Core and the domain model.
/// </summary>
public class WorkflowStepExecutionConstraintTests : IClassFixture<PostgreSqlFixture>
{
    private const string ValidStepId = "step-1";
    private const string ValidSnapshot = """{"steps":[{"id":"step-1","type":"Log"}]}""";
    private const string ValidHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly PostgreSqlFixture _fixture;
    private readonly SystemClock _clock = new();

    public WorkflowStepExecutionConstraintTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record SeededExecution(Guid WorkspaceId, Guid ExecutionId);

    /// <summary>
    /// Seeds a user, workspace, and parent execution. The execution is inserted with raw
    /// SQL because the step tests only need a valid (WorkspaceId, ExecutionId) pair for
    /// the composite foreign key, not a full workflow/version object graph.
    /// </summary>
    private async Task<SeededExecution> SeedExecutionAsync()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        using var scope = _fixture.CreateScope();
        var workspaceContext = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        workspaceContext.SetContext(workspaceId, userId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"user_{userId:N}",
            Email = $"{userId:N}@test.com"
        });
        db.Workspaces.Add(Workspace.CreatePersonal(workspaceId, userId, "Step Constraint Workspace"));
        await db.SaveChangesAsync();

        // A valid Pending execution row: no timestamps, valid snapshot and hash.
        const string sql = """
            INSERT INTO "WorkflowExecutions" (
                "Id", "WorkspaceId", "WorkflowId", "WorkflowVersionId", "WorkflowName",
                "VersionNumber", "DefinitionSchemaVersion", "DefinitionSnapshot", "DefinitionHash",
                "Status", "Source", "RequestedByUserId", "ScheduleId", "ScheduledOccurrenceId",
                "StartedAt", "CompletedAt", "FailedStepId", "FailureReason",
                "CancellationRequestedAt", "CancellationReason", "CancelledAt",
                "IsCancellable", "CreatedAt", "UpdatedAt")
            VALUES (
                {0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9},
                {10}, {11}, {12}, {13}, {14}, {15}, {16}, {17}, {18}, {19},
                {20}, {21}, {22}, {23})
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            executionId,
            workspaceId,
            Guid.NewGuid(),                 // WorkflowId
            Guid.NewGuid(),                 // WorkflowVersionId
            "Step Constraint Execution",
            1,                              // VersionNumber
            1,                              // DefinitionSchemaVersion
            ValidSnapshot,
            ValidHash,
            (int)WorkflowExecutionStatus.Pending,
            (int)WorkflowExecutionSource.Manual,
            null!,                          // RequestedByUserId
            null!,                          // ScheduleId
            null!,                          // ScheduledOccurrenceId
            null!,                          // StartedAt
            null!,                          // CompletedAt
            null!,                          // FailedStepId
            null!,                          // FailureReason
            null!,                          // CancellationRequestedAt
            null!,                          // CancellationReason
            null!,                          // CancelledAt
            true,                           // IsCancellable
            _clock.UtcNow,                  // CreatedAt
            null!);                         // UpdatedAt

        return new SeededExecution(workspaceId, executionId);
    }

    /// <summary>
    /// Inserts a complete, otherwise-valid step execution row and overrides only the
    /// fields under test. PostgreSQL must reject it with a check-constraint violation.
    /// </summary>
    private async Task<PostgresException> InsertInvalidStepExecutionAsync(
        string stepId = ValidStepId,
        int attemptNumber = 1,
        int status = (int)WorkflowStepExecutionStatus.Pending,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? cancelledAt = null,
        string? errorMessage = null,
        bool isRetryable = false,
        string? outputMetadata = null)
    {
        var seeded = await SeedExecutionAsync();

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        const string sql = """
            INSERT INTO "WorkflowStepExecutions" (
                "Id", "WorkspaceId", "ExecutionId", "StepId", "AttemptNumber", "Status",
                "StartedAt", "CompletedAt", "CancelledAt", "ErrorMessage", "IsRetryable",
                "OutputMetadata", "CreatedAt", "UpdatedAt")
            VALUES (
                {0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13})
            """;

        return await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                sql,
                Guid.NewGuid(),             // Id
                seeded.WorkspaceId,
                seeded.ExecutionId,
                stepId,
                attemptNumber,
                status,
                (object)startedAt!,
                (object)completedAt!,
                (object)cancelledAt!,
                (object)errorMessage!,
                isRetryable,
                (object)outputMetadata!,
                _clock.UtcNow,              // CreatedAt
                null!));                    // UpdatedAt
    }

    private static void AssertCheckViolation(PostgresException ex, string constraintName)
    {
        Assert.Equal("23514", ex.SqlState);
        Assert.Equal(constraintName, ex.ConstraintName);
    }

    // 1. StepId and AttemptNumber integrity

    [Fact]
    public async Task GivenUntrimmedStepId_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(stepId: "  step-1  ");

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_StepId_TrimmedAndNotEmpty");
    }

    [Fact]
    public async Task GivenEmptyStepId_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(stepId: string.Empty);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_StepId_TrimmedAndNotEmpty");
    }

    [Fact]
    public async Task GivenZeroAttemptNumber_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(attemptNumber: 0);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_AttemptNumber_Positive");
    }

    [Fact]
    public async Task GivenNegativeAttemptNumber_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(attemptNumber: -1);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_AttemptNumber_Positive");
    }

    // 2. Status enum range guard

    [Fact]
    public async Task GivenInvalidStatus_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(status: 99);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Status_Enum_Valid");
    }

    // 3. Status and timestamp consistency

    [Fact]
    public async Task GivenPendingStepWithStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Pending,
            startedAt: _clock.UtcNow);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Pending_NoTimestamps");
    }

    [Fact]
    public async Task GivenPendingStepWithCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Pending,
            completedAt: _clock.UtcNow);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Pending_NoTimestamps");
    }

    [Fact]
    public async Task GivenPendingStepWithCancelledAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Pending,
            cancelledAt: _clock.UtcNow);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Pending_NoTimestamps");
    }

    [Fact]
    public async Task GivenRunningStepWithoutStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Running);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Running_RequiresStartedAt");
    }

    [Fact]
    public async Task GivenRunningStepWithCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Running,
            startedAt: _clock.UtcNow,
            completedAt: _clock.UtcNow);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Running_RequiresStartedAt");
    }

    [Fact]
    public async Task GivenCompletedStepWithoutStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Completed,
            completedAt: _clock.UtcNow);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Completed_RequiresTimestamps");
    }

    [Fact]
    public async Task GivenCompletedStepWithoutCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Completed,
            startedAt: _clock.UtcNow);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Completed_RequiresTimestamps");
    }

    [Fact]
    public async Task GivenFailedStepWithoutErrorMessage_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Failed,
            startedAt: _clock.UtcNow,
            completedAt: _clock.UtcNow);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Failed_RequiresTimestampsAndReason");
    }

    [Fact]
    public async Task GivenFailedStepWithWhitespaceErrorMessage_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Failed,
            startedAt: _clock.UtcNow,
            completedAt: _clock.UtcNow,
            errorMessage: "   ");

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Failed_RequiresTimestampsAndReason");
    }

    [Fact]
    public async Task GivenCancelledStepWithoutCancelledAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Cancelled);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Cancelled_RequiresCancelledAt");
    }

    [Fact]
    public async Task GivenSkippedStepWithStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Skipped,
            startedAt: _clock.UtcNow,
            completedAt: _clock.UtcNow);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Skipped_RequiresCompletedAt");
    }

    [Fact]
    public async Task GivenSkippedStepWithoutCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Skipped);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_Skipped_RequiresCompletedAt");
    }

    [Fact]
    public async Task GivenCompletedStepWithCancelledAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        // Completed (not Running) isolates FinishTimestamps_Exclusive: all other
        // status/timestamp constraints are satisfied by this row.
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Completed,
            startedAt: _clock.UtcNow,
            completedAt: _clock.UtcNow,
            cancelledAt: _clock.UtcNow);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_FinishTimestamps_Exclusive");
    }

    // 4. Conditional fields

    [Fact]
    public async Task GivenErrorMessageOnCompletedStep_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Completed,
            startedAt: _clock.UtcNow,
            completedAt: _clock.UtcNow,
            errorMessage: "Err");

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_ErrorMessage_OnlyOnFailed");
    }

    [Fact]
    public async Task GivenOutputMetadataOnFailedStep_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Failed,
            startedAt: _clock.UtcNow,
            completedAt: _clock.UtcNow,
            errorMessage: "Err",
            outputMetadata: "{}");

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_OutputMetadata_OnlyOnCompleted");
    }

    [Fact]
    public async Task GivenIsRetryableTrueOnCompletedStep_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidStepExecutionAsync(
            status: (int)WorkflowStepExecutionStatus.Completed,
            startedAt: _clock.UtcNow,
            completedAt: _clock.UtcNow,
            isRetryable: true);

        AssertCheckViolation(ex, "CK_WorkflowStepExecutions_IsRetryable_OnlyOnFailed");
    }
}
