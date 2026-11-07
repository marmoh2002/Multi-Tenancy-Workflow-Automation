namespace WorkflowAutomation.Workflows.Core.Tests;

using System;
using System.Text.Json;
using WorkflowAutomation.Workflows.Core.Definitions;
using WorkflowAutomation.Workflows.Core.Entities;
using WorkflowAutomation.Workflows.Core.Enums;
using Xunit;

public class WorkflowExecutionTests
{
    private readonly TestClock _clock = new();
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();

    private Workflow CreateTestWorkflow(string name = "Order Processing") =>
        Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            name,
            "Processes customer orders",
            _clock);

    private WorkflowVersion CreatePublishedVersion(Guid workflowId, Guid? workspaceId = null)
    {
        var definition = new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition
                {
                    Id = "step-1",
                    Type = "Log"
                }
            ]
        };

        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            workspaceId ?? WorkspaceId,
            workflowId,
            1,
            definition,
            new KahnDagValidator(),
            _clock);

        version.Publish(new KahnDagValidator(), _clock);
        return version;
    }

    private WorkflowExecution CreatePendingExecution(
        Workflow? workflow = null,
        WorkflowVersion? version = null,
        WorkflowExecutionSource source = WorkflowExecutionSource.Manual,
        Guid? requestedByUserId = null)
    {
        var wf = workflow ?? CreateTestWorkflow();
        var ver = version ?? CreatePublishedVersion(wf.Id);

        return WorkflowExecution.Create(
            Guid.NewGuid(),
            wf,
            ver,
            source,
            _clock,
            requestedByUserId: requestedByUserId ?? CreatorId);
    }

    // =========================================================================
    // 1. Creation & Snapshot Invariants
    // =========================================================================

    [Fact]
    public void Create_initializes_pending_execution_with_immutable_snapshot()
    {
        var id = Guid.NewGuid();
        var workflow = CreateTestWorkflow("Invoice Flow");
        var version = CreatePublishedVersion(workflow.Id);

        var execution = WorkflowExecution.Create(
            id,
            workflow,
            version,
            WorkflowExecutionSource.Manual,
            _clock,
            requestedByUserId: CreatorId);

        Assert.Equal(id, execution.Id);
        Assert.Equal(WorkspaceId, execution.WorkspaceId);
        Assert.Equal(workflow.Id, execution.WorkflowId);
        Assert.Equal(version.Id, execution.WorkflowVersionId);
        Assert.Equal("Invoice Flow", execution.WorkflowName);
        Assert.Equal(version.VersionNumber, execution.VersionNumber);
        Assert.Equal(version.DefinitionSchemaVersion, execution.DefinitionSchemaVersion);
        Assert.Equal(version.SerializedDefinition, execution.DefinitionSnapshot);

        // Asserts valid 64-char lowercase hexadecimal SHA-256 pattern
        Assert.Matches("^[0-9a-f]{64}$", execution.DefinitionHash);

        Assert.Equal(WorkflowExecutionStatus.Pending, execution.Status);
        Assert.Equal(WorkflowExecutionSource.Manual, execution.Source);
        Assert.Equal(CreatorId, execution.RequestedByUserId);
        Assert.Null(execution.ScheduleId);
        Assert.Null(execution.ScheduledOccurrenceId);

        Assert.Null(execution.StartedAt);
        Assert.Null(execution.CompletedAt);
        Assert.Null(execution.CancelledAt);
        Assert.Null(execution.FinishedAt);
        Assert.Null(execution.FailedStepId);
        Assert.Null(execution.FailureReason);
        Assert.Null(execution.CancellationRequestedAt);
        Assert.Null(execution.CancellationReason);

        Assert.True(execution.IsCancellable);
        Assert.Equal(_clock.UtcNow, execution.CreatedAt);
        Assert.Null(execution.UpdatedAt);
        Assert.True(execution.HasIntactSnapshot());
    }

    [Fact]
    public void Create_scheduled_execution_persists_schedule_identifiers()
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);
        var scheduleId = Guid.NewGuid();
        var occurrenceId = "  cron-2026-11-05-1200  ";

        var execution = WorkflowExecution.Create(
            Guid.NewGuid(),
            workflow,
            version,
            WorkflowExecutionSource.Scheduled,
            _clock,
            scheduleId: scheduleId,
            scheduledOccurrenceId: occurrenceId);

        Assert.Equal(WorkflowExecutionSource.Scheduled, execution.Source);
        Assert.Equal(scheduleId, execution.ScheduleId);
        Assert.Equal("cron-2026-11-05-1200", execution.ScheduledOccurrenceId);
    }

    [Fact]
    public void Create_rejects_empty_id()
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);

        var ex = Assert.Throws<ArgumentException>(() =>
            WorkflowExecution.Create(
                Guid.Empty,
                workflow,
                version,
                WorkflowExecutionSource.Manual,
                _clock));

        Assert.Contains("Execution ID cannot be empty", ex.Message);
    }

    [Fact]
    public void Create_rejects_unpublished_version()
    {
        var workflow = CreateTestWorkflow();
        var draftVersion = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            workflow.Id,
            1,
            new WorkflowDefinition { Steps = [new StepDefinition { Id = "s1", Type = "Log" }] },
            new KahnDagValidator(),
            _clock);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                draftVersion,
                WorkflowExecutionSource.Manual,
                _clock));

        Assert.Contains("unpublished workflow version", ex.Message);
    }

    [Fact]
    public void Create_rejects_version_belonging_to_different_workflow()
    {
        var workflowA = CreateTestWorkflow();
        var workflowB = CreateTestWorkflow();
        var versionB = CreatePublishedVersion(workflowB.Id);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflowA,
                versionB,
                WorkflowExecutionSource.Manual,
                _clock));

        Assert.Contains("does not belong to the target workflow", ex.Message);
    }

    [Fact]
    public void Create_rejects_version_belonging_to_different_workspace()
    {
        var workflow = CreateTestWorkflow();
        var differentWorkspaceId = Guid.NewGuid();
        var versionOtherWorkspace = CreatePublishedVersion(workflow.Id, differentWorkspaceId);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                versionOtherWorkspace,
                WorkflowExecutionSource.Manual,
                _clock));

        Assert.Contains("same workspace", ex.Message);
    }

    [Fact]
    public void Create_rejects_undefined_source()
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                (WorkflowExecutionSource)999,
                _clock));
    }

    [Fact]
    public void Create_rejects_empty_guid_identifiers()
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);

        Assert.Throws<ArgumentException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Manual,
                _clock,
                requestedByUserId: Guid.Empty));

        Assert.Throws<ArgumentException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Scheduled,
                _clock,
                scheduleId: Guid.Empty,
                scheduledOccurrenceId: "occ-1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_snapshot(string blankSnapshot)
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);

        typeof(WorkflowVersion)
            .GetProperty(nameof(WorkflowVersion.SerializedDefinition))!
            .SetValue(version, blankSnapshot);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Manual,
                _clock));

        Assert.Contains("snapshot is empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_rejects_non_deserializable_snapshot()
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);

        typeof(WorkflowVersion)
            .GetProperty(nameof(WorkflowVersion.SerializedDefinition))!
            .SetValue(version, "{ not-valid-json }");

        Assert.ThrowsAny<JsonException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Manual,
                _clock));
    }

    [Fact]
    public void Create_rejects_unsupported_schema_version()
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);

        typeof(WorkflowVersion)
            .GetProperty(nameof(WorkflowVersion.DefinitionSchemaVersion))!
            .SetValue(version, 999);

        Assert.Throws<NotSupportedException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Manual,
                _clock));
    }

    [Fact]
    public void Create_scheduled_execution_requires_schedule_and_occurrence_id()
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);

        // Missing scheduleId
        Assert.Throws<ArgumentException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Scheduled,
                _clock,
                scheduleId: null,
                scheduledOccurrenceId: "occ-1"));

        // Missing occurrenceId
        Assert.Throws<ArgumentException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Scheduled,
                _clock,
                scheduleId: Guid.NewGuid(),
                scheduledOccurrenceId: "   "));
    }

    [Fact]
    public void Create_scheduled_execution_rejects_overlong_occurrence_id()
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);
        var overlongOccurrence = new string('o', 201);

        var ex = Assert.Throws<ArgumentException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Scheduled,
                _clock,
                scheduleId: Guid.NewGuid(),
                scheduledOccurrenceId: overlongOccurrence));

        Assert.Contains("cannot exceed 200 characters", ex.Message);
    }

    [Fact]
    public void Create_manual_or_webhook_rejects_schedule_parameters()
    {
        var workflow = CreateTestWorkflow();
        var version = CreatePublishedVersion(workflow.Id);

        Assert.Throws<ArgumentException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Manual,
                _clock,
                scheduleId: Guid.NewGuid()));

        Assert.Throws<ArgumentException>(() =>
            WorkflowExecution.Create(
                Guid.NewGuid(),
                workflow,
                version,
                WorkflowExecutionSource.Webhook,
                _clock,
                scheduledOccurrenceId: "occ-123"));
    }

    // =========================================================================
    // 2. Start Transition
    // =========================================================================

    [Fact]
    public void Start_transitions_pending_to_running_and_sets_timestamps()
    {
        // Clock advances 1 second on EVERY read of UtcNow
        var advancingClock = new TestClock(advancePerRead: TimeSpan.FromSeconds(1));
        var execution = CreatePendingExecution();

        var initialReads = advancingClock.ReadsCount;
        execution.Start(advancingClock);

        Assert.Equal(WorkflowExecutionStatus.Running, execution.Status);
        // Proves single clock read: if read twice, StartedAt and UpdatedAt would differ by 1 second!
        Assert.Equal(execution.StartedAt, execution.UpdatedAt);
        Assert.Equal(initialReads + 1, advancingClock.ReadsCount);
    }

    [Fact]
    public void Start_rejects_execution_with_pending_cancellation_request()
    {
        var execution = CreatePendingExecution();
        execution.RequestCancellation("Aborted by user", _clock);

        var ex = Assert.Throws<InvalidOperationException>(() => execution.Start(_clock));
        Assert.Contains("pending cancellation request", ex.Message);
        Assert.Equal(WorkflowExecutionStatus.Pending, execution.Status);
    }

    [Fact]
    public void Start_rejects_non_pending_executions()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        var ex = Assert.Throws<InvalidOperationException>(() => execution.Start(_clock));
        Assert.Contains("Only Pending executions can be started", ex.Message);
    }
    [Fact]
    public void Start_rejects_all_non_pending_statuses()
    {
        // 1. From Running
        var executionRunning = CreatePendingExecution();
        executionRunning.Start(_clock);
        Assert.Throws<InvalidOperationException>(() => executionRunning.Start(_clock));

        // 2. From Completed
        var executionCompleted = CreatePendingExecution();
        executionCompleted.Start(_clock);
        executionCompleted.Complete(_clock);
        Assert.Throws<InvalidOperationException>(() => executionCompleted.Start(_clock));

        // 3. From Failed
        var executionFailed = CreatePendingExecution();
        executionFailed.Start(_clock);
        executionFailed.Fail("s1", "Crash", _clock);
        Assert.Throws<InvalidOperationException>(() => executionFailed.Start(_clock));

        // 4. From Cancelled
        var executionCancelled = CreatePendingExecution();
        executionCancelled.Start(_clock);
        executionCancelled.RequestCancellation("Stop", _clock);
        executionCancelled.MarkCancelled(_clock);
        Assert.Throws<InvalidOperationException>(() => executionCancelled.Start(_clock));
    }

    [Fact]
    public void PreventCancellation_rejects_all_non_running_or_already_requested_executions()
    {
        // 1. From Pending
        var executionPending = CreatePendingExecution();
        Assert.Throws<InvalidOperationException>(() => executionPending.PreventCancellation(_clock));

        // 2. From Completed
        var executionCompleted = CreatePendingExecution();
        executionCompleted.Start(_clock);
        executionCompleted.Complete(_clock);
        Assert.Throws<InvalidOperationException>(() => executionCompleted.PreventCancellation(_clock));

        // 3. From Failed
        var executionFailed = CreatePendingExecution();
        executionFailed.Start(_clock);
        executionFailed.Fail("s1", "Crash", _clock);
        Assert.Throws<InvalidOperationException>(() => executionFailed.PreventCancellation(_clock));

        // 4. From Cancelled
        var executionCancelled = CreatePendingExecution();
        executionCancelled.Start(_clock);
        executionCancelled.RequestCancellation("Stop", _clock);
        executionCancelled.MarkCancelled(_clock);
        Assert.Throws<InvalidOperationException>(() => executionCancelled.PreventCancellation(_clock));

        // 5. From Running but already requested
        var executionRunning = CreatePendingExecution();
        executionRunning.Start(_clock);
        executionRunning.RequestCancellation("Stop", _clock);
        var ex = Assert.Throws<InvalidOperationException>(() => executionRunning.PreventCancellation(_clock));
        Assert.Contains("after a cancellation request has already been recorded", ex.Message);
    }
    // =========================================================================
    // 3. Complete Transition & Race Precedence
    // =========================================================================

    [Fact]
    public void Complete_transitions_running_to_completed()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        _clock.Advance(TimeSpan.FromSeconds(10));
        execution.Complete(_clock);

        Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status);
        Assert.Equal(_clock.UtcNow, execution.CompletedAt);
        Assert.Equal(_clock.UtcNow, execution.FinishedAt);
        Assert.Equal(_clock.UtcNow, execution.UpdatedAt);
    }

    [Fact]
    public void Complete_clears_pending_cancellation_when_completion_wins_race()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        // User requested cancellation while running
        execution.RequestCancellation("Stop before done", _clock);
        Assert.NotNull(execution.CancellationRequestedAt);
        Assert.NotNull(execution.CancellationReason);

        // Worker finished before cancellation settled: Completion wins!
        _clock.Advance(TimeSpan.FromSeconds(2));
        execution.Complete(_clock);

        Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status);
        Assert.Null(execution.CancellationRequestedAt);
        Assert.Null(execution.CancellationReason);
        Assert.Equal(_clock.UtcNow, execution.CompletedAt);
        Assert.Equal(_clock.UtcNow, execution.FinishedAt);
    }

    [Fact]
    public void Complete_rejects_all_non_running_statuses()
    {
        // 1. From Pending
        var executionPending = CreatePendingExecution();
        Assert.Throws<InvalidOperationException>(() => executionPending.Complete(_clock));

        // 2. From Completed
        var executionCompleted = CreatePendingExecution();
        executionCompleted.Start(_clock);
        executionCompleted.Complete(_clock);
        Assert.Throws<InvalidOperationException>(() => executionCompleted.Complete(_clock));

        // 3. From Failed
        var executionFailed = CreatePendingExecution();
        executionFailed.Start(_clock);
        executionFailed.Fail("s1", "Crash", _clock);
        Assert.Throws<InvalidOperationException>(() => executionFailed.Complete(_clock));

        // 4. From Cancelled
        var executionCancelled = CreatePendingExecution();
        executionCancelled.Start(_clock);
        executionCancelled.RequestCancellation("Stop", _clock);
        executionCancelled.MarkCancelled(_clock);
        Assert.Throws<InvalidOperationException>(() => executionCancelled.Complete(_clock));
    }

    // =========================================================================
    // 4. Fail Transition (Pending & Running)
    // =========================================================================

    [Fact]
    public void Fail_from_running_transitions_to_failed_and_trims_inputs()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        _clock.Advance(TimeSpan.FromSeconds(3));
        execution.Fail("  step-1  ", "  Database connection failed  ", _clock);

        Assert.Equal(WorkflowExecutionStatus.Failed, execution.Status);
        Assert.Equal("step-1", execution.FailedStepId);
        Assert.Equal("Database connection failed", execution.FailureReason);
        Assert.Equal(_clock.UtcNow, execution.CompletedAt);
        Assert.Equal(_clock.UtcNow, execution.FinishedAt);
        Assert.Equal(_clock.UtcNow, execution.UpdatedAt);
    }

    [Fact]
    public void Fail_from_pending_allows_dispatch_or_startup_failures()
    {
        var execution = CreatePendingExecution();
        _clock.Advance(TimeSpan.FromSeconds(1));

        // Failed before starting (e.g. queue dispatch failed)
        execution.Fail(null, "Worker queue unreachable", _clock);

        Assert.Equal(WorkflowExecutionStatus.Failed, execution.Status);
        Assert.Null(execution.FailedStepId);
        Assert.Equal("Worker queue unreachable", execution.FailureReason);
        Assert.Null(execution.StartedAt);
        Assert.Equal(_clock.UtcNow, execution.CompletedAt);
        Assert.Equal(_clock.UtcNow, execution.FinishedAt);
    }

    [Fact]
    public void Fail_normalizes_empty_failed_step_id_to_null()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        execution.Fail("   ", "Reason", _clock);
        Assert.Null(execution.FailedStepId);
    }

    [Fact]
    public void Fail_rejects_overlong_failed_step_id()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);
        var overlongStepId = new string('s', 101);

        var ex = Assert.Throws<ArgumentException>(() =>
            execution.Fail(overlongStepId, "Reason", _clock));

        Assert.Contains("cannot exceed 100 characters", ex.Message);
    }

    [Fact]
    public void Fail_clears_pending_cancellation_when_failure_wins_race()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        execution.RequestCancellation("Cancel requested", _clock);
        Assert.NotNull(execution.CancellationRequestedAt);

        // Crash occurred before cancellation was finalized
        execution.Fail("step-2", "Out of memory", _clock);

        Assert.Equal(WorkflowExecutionStatus.Failed, execution.Status);
        Assert.Null(execution.CancellationRequestedAt);
        Assert.Null(execution.CancellationReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fail_rejects_missing_or_whitespace_reason(string? reason)
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        Assert.Throws<ArgumentException>(() => execution.Fail("step-1", reason!, _clock));
    }

    [Fact]
    public void Fail_rejects_all_terminal_statuses()
    {
        // 1. From Completed
        var executionCompleted = CreatePendingExecution();
        executionCompleted.Start(_clock);
        executionCompleted.Complete(_clock);
        Assert.Throws<InvalidOperationException>(() => executionCompleted.Fail("s1", "Late", _clock));

        // 2. From Failed
        var executionFailed = CreatePendingExecution();
        executionFailed.Start(_clock);
        executionFailed.Fail("s1", "Crash", _clock);
        Assert.Throws<InvalidOperationException>(() => executionFailed.Fail("s1", "Late", _clock));

        // 3. From Cancelled
        var executionCancelled = CreatePendingExecution();
        executionCancelled.Start(_clock);
        executionCancelled.RequestCancellation("Stop", _clock);
        executionCancelled.MarkCancelled(_clock);
        Assert.Throws<InvalidOperationException>(() => executionCancelled.Fail("s1", "Late", _clock));
    }

    // =========================================================================
    // 5. Cancellation Idempotency & Point of No Return
    // =========================================================================

    [Fact]
    public void RequestCancellation_records_request_on_active_execution()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        _clock.Advance(TimeSpan.FromSeconds(2));
        execution.RequestCancellation("  User requested stop  ", _clock);

        Assert.Equal(WorkflowExecutionStatus.Running, execution.Status);
        Assert.Equal(_clock.UtcNow, execution.CancellationRequestedAt);
        Assert.Equal("User requested stop", execution.CancellationReason);
        Assert.Equal(_clock.UtcNow, execution.UpdatedAt);
    }

    [Fact]
    public void RequestCancellation_is_idempotent_on_retries_and_preserves_initial_data()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        var initialTime = _clock.UtcNow;
        execution.RequestCancellation("First reason", _clock);

        _clock.Advance(TimeSpan.FromSeconds(10));

        // Duplicate retried delivery (even with empty reason or different reason) is a safe no-op
        execution.RequestCancellation("Second reason", _clock);
        execution.RequestCancellation("   ", _clock);

        Assert.Equal(initialTime, execution.CancellationRequestedAt);
        Assert.Equal("First reason", execution.CancellationReason);
    }

    [Fact]
    public void RequestCancellation_idempotent_retry_leaves_updated_at_unchanged()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        _clock.Advance(TimeSpan.FromSeconds(2));
        execution.RequestCancellation("Initial cancel", _clock);
        var initialUpdatedAt = execution.UpdatedAt;

        _clock.Advance(TimeSpan.FromSeconds(10));
        execution.RequestCancellation("Duplicate retry", _clock);

        // Asserts no-op retry does not mutate UpdatedAt
        Assert.Equal(initialUpdatedAt, execution.UpdatedAt);
    }

    [Fact]
    public void RequestCancellation_is_noop_if_already_cancelled()
    {
        var execution = CreatePendingExecution();
        execution.RequestCancellation("Reason", _clock);
        execution.MarkCancelled(_clock);

        Assert.Equal(WorkflowExecutionStatus.Cancelled, execution.Status);

        // Retrying cancellation on an already cancelled execution returns cleanly
        execution.RequestCancellation("Reason retry", _clock);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, execution.Status);
    }

    [Fact]
    public void RequestCancellation_rejects_all_terminal_statuses()
    {
        // 1. From Completed
        var executionCompleted = CreatePendingExecution();
        executionCompleted.Start(_clock);
        executionCompleted.Complete(_clock);
        Assert.Throws<InvalidOperationException>(() => executionCompleted.RequestCancellation("Stop", _clock));

        // 2. From Failed
        var executionFailed = CreatePendingExecution();
        executionFailed.Start(_clock);
        executionFailed.Fail("s1", "Crash", _clock);
        Assert.Throws<InvalidOperationException>(() => executionFailed.RequestCancellation("Stop", _clock));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestCancellation_rejects_missing_or_whitespace_reason_on_first_request(string? reason)
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        Assert.Throws<ArgumentException>(() => execution.RequestCancellation(reason!, _clock));
    }

    [Fact]
    public void PreventCancellation_locks_execution_against_cancellation_and_updates_timestamp()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        Assert.True(execution.IsCancellable);

        _clock.Advance(TimeSpan.FromSeconds(3));
        execution.PreventCancellation(_clock);

        Assert.False(execution.IsCancellable);
        Assert.Equal(_clock.UtcNow, execution.UpdatedAt);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            execution.RequestCancellation("Try to stop", _clock));

        Assert.Contains("passed the point of no return", ex.Message);
    }

    [Fact]
    public void MarkCancelled_transitions_to_cancelled_with_timestamps()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);
        execution.RequestCancellation("User aborted", _clock);

        _clock.Advance(TimeSpan.FromSeconds(4));
        execution.MarkCancelled(_clock);

        Assert.Equal(WorkflowExecutionStatus.Cancelled, execution.Status);
        Assert.Equal(_clock.UtcNow, execution.CancelledAt);
        Assert.Equal(_clock.UtcNow, execution.FinishedAt);
        Assert.Equal(_clock.UtcNow, execution.UpdatedAt);
    }

    [Fact]
    public void MarkCancelled_requires_prior_cancellation_request()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);

        var ex = Assert.Throws<InvalidOperationException>(() => execution.MarkCancelled(_clock));
        Assert.Contains("without an existing cancellation request", ex.Message);
    }

    // =========================================================================
    // 6. Snapshot Integrity & Immutability Across Transitions
    // =========================================================================

    [Fact]
    public void Snapshot_fields_remain_immutable_across_lifecycle_transitions()
    {
        var execution = CreatePendingExecution();
        var initialName = execution.WorkflowName;
        var initialVersionNumber = execution.VersionNumber;
        var initialSchemaVersion = execution.DefinitionSchemaVersion;
        var initialSnapshot = execution.DefinitionSnapshot;
        var initialHash = execution.DefinitionHash;

        _clock.Advance(TimeSpan.FromSeconds(1));
        execution.Start(_clock);

        _clock.Advance(TimeSpan.FromSeconds(1));
        execution.PreventCancellation(_clock);

        _clock.Advance(TimeSpan.FromSeconds(1));
        execution.Complete(_clock);

        // Asserts snapshot fields never mutated across transitions
        Assert.Equal(initialName, execution.WorkflowName);
        Assert.Equal(initialVersionNumber, execution.VersionNumber);
        Assert.Equal(initialSchemaVersion, execution.DefinitionSchemaVersion);
        Assert.Equal(initialSnapshot, execution.DefinitionSnapshot);
        Assert.Equal(initialHash, execution.DefinitionHash);
    }

    [Fact]
    public void HasIntactSnapshot_and_GetDefinition_succeed_for_valid_snapshot()
    {
        var execution = CreatePendingExecution();

        Assert.True(execution.HasIntactSnapshot());

        var definition = execution.GetDefinition();
        Assert.NotNull(definition);
        Assert.Single(definition.Steps);
        Assert.Equal("step-1", definition.Steps[0].Id);
    }
}
