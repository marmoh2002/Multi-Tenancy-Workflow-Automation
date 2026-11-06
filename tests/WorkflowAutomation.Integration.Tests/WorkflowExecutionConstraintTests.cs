namespace WorkflowAutomation.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Infrastructure.Persistence;
using WorkflowAutomation.Integration.Tests.Fixtures;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Shared.Kernel.Time;
using WorkflowAutomation.Workspaces.Core.Entities;
using Xunit;

public class WorkflowExecutionConstraintTests : IClassFixture<PostgreSqlFixture>
{
    private const string ValidSnapshot = """{"steps":[{"id":"step-1","type":"Log"}]}""";
    private const string ValidHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly PostgreSqlFixture _fixture;
    private readonly SystemClock _clock = new();

    public WorkflowExecutionConstraintTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<Guid> SeedWorkspaceAsync()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

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
        db.Workspaces.Add(Workspace.CreatePersonal(workspaceId, userId, "Constraint Workspace"));
        await db.SaveChangesAsync();

        return workspaceId;
    }

    /// <summary>
    /// Inserts a WorkflowExecution row directly, bypassing EF Core and the domain,
    /// so PostgreSQL check constraints are exercised by the database engine itself.
    /// </summary>
    private async Task<PostgresException> InsertInvalidExecutionAsync(
        string? workflowName = null,
        int versionNumber = 1,
        int definitionSchemaVersion = 1,
        string? definitionSnapshot = null,
        string? definitionHash = null,
        int status = 1,
        int source = 1,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        string? failureReason = null,
        string? failedStepId = null,
        DateTimeOffset? cancellationRequestedAt = null,
        string? cancellationReason = null,
        DateTimeOffset? cancelledAt = null)
    {
        var workspaceId = await SeedWorkspaceAsync();

        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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

        return await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                sql,
                Guid.NewGuid(),                 // Id
                workspaceId,                    // WorkspaceId
                Guid.NewGuid(),                 // WorkflowId
                Guid.NewGuid(),                 // WorkflowVersionId
                workflowName ?? "Constraint Workflow",
                versionNumber,
                definitionSchemaVersion,
                definitionSnapshot ?? ValidSnapshot,
                definitionHash ?? ValidHash,
                status,
                source,
                null!,                          // RequestedByUserId
                null!,                          // ScheduleId
                null!,                          // ScheduledOccurrenceId
                (object)startedAt!,
                (object)completedAt!,
                (object)failedStepId!,
                (object)failureReason!,
                (object)cancellationRequestedAt!,
                (object)cancellationReason!,
                (object)cancelledAt!,
                true,                           // IsCancellable
                _clock.UtcNow,                  // CreatedAt
                null!));                        // UpdatedAt
    }

    [Fact]
    public async Task GivenUntrimmedWorkflowName_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(workflowName: "  Untrimmed Name  ");

        Assert.Equal("CK_WorkflowExecutions_WorkflowName_TrimmedAndNotEmpty", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenEmptyWorkflowName_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(workflowName: string.Empty);

        Assert.Equal("CK_WorkflowExecutions_WorkflowName_TrimmedAndNotEmpty", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenNonPositiveVersionNumber_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(versionNumber: 0);

        Assert.Equal("CK_WorkflowExecutions_VersionNumber_Positive", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenNonPositiveDefinitionSchemaVersion_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(definitionSchemaVersion: 0);

        Assert.Equal("CK_WorkflowExecutions_DefinitionSchemaVersion_Positive", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenEmptyDefinitionSnapshot_WhenInsertedRaw_IsRejectedByPostgreSql()
    {
        var ex = await InsertInvalidExecutionAsync(definitionSnapshot: string.Empty);

        // An empty text violates the NotEmpty check, while the jsonb cast may
        // report invalid JSON first depending on constraint evaluation order.
        Assert.True(
            ex.SqlState is "23514" or "22P02",
            $"Expected a check-constraint or invalid-JSON failure, got SQLSTATE {ex.SqlState}.");
    }

    [Fact]
    public async Task GivenNonObjectDefinitionSnapshot_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(definitionSnapshot: "[]");

        Assert.Equal("CK_WorkflowExecutions_DefinitionSnapshot_IsObject", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenInvalidJsonDefinitionSnapshot_WhenInsertedRaw_IsRejectedByPostgreSql()
    {
        var ex = await InsertInvalidExecutionAsync(definitionSnapshot: "not-json");

        Assert.Equal("22P02", ex.SqlState);
    }

    [Fact]
    public async Task GivenMalformedDefinitionHash_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(definitionHash: "not-a-sha256-hash");

        Assert.Equal("CK_WorkflowExecutions_DefinitionHash_Sha256Hex", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenUppercaseDefinitionHash_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(definitionHash: new string('A', 64));

        Assert.Equal("CK_WorkflowExecutions_DefinitionHash_Sha256Hex", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenRunningExecutionWithoutStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(status: 2);

        Assert.Equal("CK_WorkflowExecutions_Running_RequiresStartedAt", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenCompletedExecutionWithoutCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(status: 3);

        Assert.Equal("CK_WorkflowExecutions_Completed_RequiresCompletedAt", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenFailedExecutionWithoutFailureReason_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(
            status: 4,
            completedAt: _clock.UtcNow);

        Assert.Equal("CK_WorkflowExecutions_Failed_RequiresCompletedAtAndReason", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenCancelledExecutionWithoutCancelledAt_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(
            status: 5,
            cancellationRequestedAt: _clock.UtcNow,
            cancellationReason: "Cancelled by test.");

        Assert.Equal("CK_WorkflowExecutions_Cancelled_RequiresTimestamps", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenCancellationRequestWithoutReason_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(
            status: 1,
            cancellationRequestedAt: _clock.UtcNow);

        Assert.Equal("CK_WorkflowExecutions_CancellationRequest_RequiresReason", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenFailedStepIdOnNonFailedExecution_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(
            status: 1,
            failedStepId: "step-1");

        Assert.Equal("CK_WorkflowExecutions_FailedStepId_OnlyOnFailed", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenInvalidExecutionStatus_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(status: 99);

        Assert.Equal("CK_WorkflowExecutions_Status_Enum_Valid", ex.ConstraintName);
    }

    [Fact]
    public async Task GivenInvalidExecutionSource_WhenInsertedRaw_ThrowsCheckConstraintViolation()
    {
        var ex = await InsertInvalidExecutionAsync(source: 99);

        Assert.Equal("CK_WorkflowExecutions_Source_Enum_Valid", ex.ConstraintName);
    }
}
