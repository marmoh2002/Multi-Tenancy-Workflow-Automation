namespace WorkflowAutomation.Workflows.Core.Tests;

using System;
using WorkflowAutomation.Workflows.Core.Definitions;
using WorkflowAutomation.Workflows.Core.Entities;
using WorkflowAutomation.Workflows.Core.Enums;
using Xunit;

public class WorkflowStepExecutionTests
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
                },
                new StepDefinition
                {
                    Id = "step-2",
                    Type = "Email"
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
        WorkflowVersion? version = null)
    {
        var wf = workflow ?? CreateTestWorkflow();
        var ver = version ?? CreatePublishedVersion(wf.Id);

        return WorkflowExecution.Create(
            Guid.NewGuid(),
            wf,
            ver,
            WorkflowExecutionSource.Manual,
            _clock,
            requestedByUserId: CreatorId);
    }

    private WorkflowExecution CreateRunningExecution()
    {
        var execution = CreatePendingExecution();
        execution.Start(_clock);
        return execution;
    }

    private WorkflowStepExecution CreatePendingStep(
        WorkflowExecution? execution = null,
        string stepId = "step-1",
        int attemptNumber = 1)
    {
        var exec = execution ?? CreateRunningExecution();
        return exec.CreateStepExecution(Guid.NewGuid(), stepId, attemptNumber, _clock);
    }

    private WorkflowStepExecution CreateRunningStep(
        WorkflowExecution? execution = null,
        string stepId = "step-1",
        int attemptNumber = 1)
    {
        var step = CreatePendingStep(execution, stepId, attemptNumber);
        step.Start(_clock);
        return step;
    }

    // =========================================================================
    // 1. Creation & Aggregate Invariants
    // =========================================================================

    [Fact]
    public void CreateStepExecution_initializes_pending_step_execution_with_correct_fields()
    {
        var execution = CreateRunningExecution();
        var stepExecId = Guid.NewGuid();
        var createdAt = _clock.UtcNow;

        var step = execution.CreateStepExecution(stepExecId, "step-1", 1, _clock);

        Assert.Equal(stepExecId, step.Id);
        Assert.Equal(execution.WorkspaceId, step.WorkspaceId);
        Assert.Equal(execution.Id, step.ExecutionId);
        Assert.Equal("step-1", step.StepId);
        Assert.Equal(1, step.AttemptNumber);
        Assert.Equal(WorkflowStepExecutionStatus.Pending, step.Status);
        Assert.Null(step.StartedAt);
        Assert.Null(step.CompletedAt);
        Assert.Null(step.CancelledAt);
        Assert.Null(step.FinishedAt);
        Assert.Null(step.ErrorMessage);
        Assert.False(step.IsRetryable);
        Assert.Null(step.OutputMetadata);
        Assert.Equal(createdAt, step.CreatedAt);
        Assert.Null(step.UpdatedAt);
    }

    [Fact]
    public void CreateStepExecution_normalizes_and_trims_step_id()
    {
        var execution = CreateRunningExecution();

        var step = execution.CreateStepExecution(Guid.NewGuid(), "  step-1  ", 1, _clock);

        Assert.Equal("step-1", step.StepId);
    }

    [Theory]
    [InlineData(false)] // Pending parent
    [InlineData(true)]  // Running parent
    public void CreateStepExecution_succeeds_when_execution_is_pending_or_running(bool startExecution)
    {
        var execution = CreatePendingExecution();
        if (startExecution)
        {
            execution.Start(_clock);
        }

        var step = execution.CreateStepExecution(Guid.NewGuid(), "step-1", 1, _clock);

        Assert.NotNull(step);
        Assert.Equal(WorkflowStepExecutionStatus.Pending, step.Status);
    }

    // =========================================================================
    // 2. Creation Guards
    // =========================================================================

    [Fact]
    public void CreateStepExecution_rejects_empty_id()
    {
        var execution = CreateRunningExecution();

        var ex = Assert.Throws<ArgumentException>(() =>
            execution.CreateStepExecution(Guid.Empty, "step-1", 1, _clock));

        Assert.Contains("Step execution ID cannot be empty", ex.Message);
    }

    [Fact]
    public void CreateStepExecution_rejects_nonexistent_step_in_definition()
    {
        var execution = CreateRunningExecution();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), "unknown-step", 1, _clock));

        Assert.Contains("Step 'unknown-step' does not exist in the pinned workflow definition", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateStepExecution_rejects_null_empty_or_whitespace_step_id(string? invalidStepId)
    {
        var execution = CreateRunningExecution();

        Assert.ThrowsAny<ArgumentException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), invalidStepId!, 1, _clock));
    }

    [Fact]
    public void CreateStepExecution_rejects_step_id_exceeding_max_length()
    {
        var execution = CreateRunningExecution();
        var longStepId = new string('a', StepDefinition.MaxIdLength + 1);

        var ex = Assert.Throws<ArgumentException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), longStepId, 1, _clock));

        Assert.Contains($"Step ID cannot exceed {StepDefinition.MaxIdLength} characters", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-42)]
    public void CreateStepExecution_rejects_non_positive_attempt_number(int invalidAttempt)
    {
        var execution = CreateRunningExecution();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), "step-1", invalidAttempt, _clock));

        Assert.Equal("attemptNumber", ex.ParamName);
    }

    [Fact]
    public void CreateStepExecution_rejects_null_clock()
    {
        var execution = CreateRunningExecution();

        Assert.Throws<ArgumentNullException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), "step-1", 1, null!));
    }

    [Fact]
    public void CreateStepExecution_rejects_completed_execution()
    {
        var execution = CreateRunningExecution();
        execution.Complete(_clock);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), "step-1", 1, _clock));

        Assert.Contains("Cannot create a step execution for an execution in 'Completed' status", ex.Message);
    }

    [Fact]
    public void CreateStepExecution_rejects_failed_execution()
    {
        var execution = CreateRunningExecution();
        execution.Fail("step-1", "Something blew up", _clock);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), "step-1", 1, _clock));

        Assert.Contains("Cannot create a step execution for an execution in 'Failed' status", ex.Message);
    }

    [Fact]
    public void CreateStepExecution_rejects_cancelled_execution()
    {
        var execution = CreateRunningExecution();
        execution.RequestCancellation("User requested cancel", _clock);
        execution.MarkCancelled(_clock);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), "step-1", 1, _clock));

        Assert.Contains("Cannot create a step execution for an execution in 'Cancelled' status", ex.Message);
    }

    [Fact]
    public void CreateStepExecution_rejects_pending_execution_with_cancellation_request()
    {
        var execution = CreatePendingExecution();
        execution.RequestCancellation("Cancel requested before start", _clock);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), "step-1", 1, _clock));

        Assert.Contains("Cannot create a step execution for an execution with a pending cancellation request", ex.Message);
    }

    [Fact]
    public void CreateStepExecution_rejects_running_execution_with_cancellation_request()
    {
        var execution = CreateRunningExecution();
        execution.RequestCancellation("Cancel requested while running", _clock);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), "step-1", 1, _clock));

        Assert.Contains("Cannot create a step execution for an execution with a pending cancellation request", ex.Message);
    }

    [Fact]
    public void CreateStepExecution_rejects_tampered_snapshot()
    {
        var execution = CreateRunningExecution();
        typeof(WorkflowExecution)
            .GetProperty(nameof(WorkflowExecution.DefinitionHash))!
            .SetValue(execution, "tampered-hash-0123456789abcdef0123456789abcdef0123456789abcdef01234567");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            execution.CreateStepExecution(Guid.NewGuid(), "step-1", 1, _clock));

        Assert.Contains("definition snapshot integrity check failed", ex.Message);
    }

    // =========================================================================
    // 3. Start Transition
    // =========================================================================

    [Fact]
    public void Start_transitions_from_pending_to_running_and_sets_timestamps()
    {
        var step = CreatePendingStep();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var startTime = _clock.UtcNow;

        step.Start(_clock);

        Assert.Equal(WorkflowStepExecutionStatus.Running, step.Status);
        Assert.Equal(startTime, step.StartedAt);
        Assert.Equal(startTime, step.UpdatedAt);
    }

    [Fact]
    public void Start_reads_clock_exactly_once()
    {
        var step = CreatePendingStep();
        var readsBefore = _clock.ReadsCount;

        step.Start(_clock);

        Assert.Equal(readsBefore + 1, _clock.ReadsCount);
    }

    [Fact]
    public void Start_rejects_null_clock()
    {
        var step = CreatePendingStep();

        Assert.Throws<ArgumentNullException>(() => step.Start(null!));
    }

    [Fact]
    public void Start_rejects_non_pending_step_executions()
    {
        var runningStep = CreateRunningStep();
        var exRunning = Assert.Throws<InvalidOperationException>(() => runningStep.Start(_clock));
        Assert.Contains("Only Pending step executions can be started", exRunning.Message);

        var completedStep = CreateRunningStep();
        completedStep.Complete(null, _clock);
        var exCompleted = Assert.Throws<InvalidOperationException>(() => completedStep.Start(_clock));
        Assert.Contains("Only Pending step executions can be started", exCompleted.Message);

        var failedStep = CreateRunningStep();
        failedStep.Fail("Error", false, _clock);
        var exFailed = Assert.Throws<InvalidOperationException>(() => failedStep.Start(_clock));
        Assert.Contains("Only Pending step executions can be started", exFailed.Message);

        var cancelledStep = CreatePendingStep();
        cancelledStep.Cancel(_clock);
        var exCancelled = Assert.Throws<InvalidOperationException>(() => cancelledStep.Start(_clock));
        Assert.Contains("Only Pending step executions can be started", exCancelled.Message);

        var skippedStep = CreatePendingStep();
        skippedStep.Skip(_clock);
        var exSkipped = Assert.Throws<InvalidOperationException>(() => skippedStep.Start(_clock));
        Assert.Contains("Only Pending step executions can be started", exSkipped.Message);
    }

    // =========================================================================
    // 4. Complete Transition
    // =========================================================================

    [Fact]
    public void Complete_transitions_from_running_to_completed_with_output_metadata()
    {
        var step = CreateRunningStep();
        _clock.Advance(TimeSpan.FromMinutes(2));
        var completeTime = _clock.UtcNow;
        const string metadata = "{\"processed\": 100}";

        step.Complete(metadata, _clock);

        Assert.Equal(WorkflowStepExecutionStatus.Completed, step.Status);
        Assert.Equal(completeTime, step.CompletedAt);
        Assert.Equal(completeTime, step.FinishedAt);
        Assert.Equal(metadata, step.OutputMetadata);
        Assert.Equal(completeTime, step.UpdatedAt);
    }

    [Fact]
    public void Complete_accepts_null_output_metadata()
    {
        var step = CreateRunningStep();

        step.Complete(null, _clock);

        Assert.Equal(WorkflowStepExecutionStatus.Completed, step.Status);
        Assert.Null(step.OutputMetadata);
    }

    [Fact]
    public void Complete_reads_clock_exactly_once()
    {
        var step = CreateRunningStep();
        var readsBefore = _clock.ReadsCount;

        step.Complete("{}", _clock);

        Assert.Equal(readsBefore + 1, _clock.ReadsCount);
    }

    [Fact]
    public void Complete_rejects_null_clock()
    {
        var step = CreateRunningStep();

        Assert.Throws<ArgumentNullException>(() => step.Complete("{}", null!));
    }

    [Fact]
    public void Complete_rejects_non_running_step_executions()
    {
        var pendingStep = CreatePendingStep();
        var exPending = Assert.Throws<InvalidOperationException>(() => pendingStep.Complete(null, _clock));
        Assert.Contains("Only Running step executions can be completed", exPending.Message);

        var completedStep = CreateRunningStep();
        completedStep.Complete(null, _clock);
        var exCompleted = Assert.Throws<InvalidOperationException>(() => completedStep.Complete(null, _clock));
        Assert.Contains("Only Running step executions can be completed", exCompleted.Message);

        var failedStep = CreateRunningStep();
        failedStep.Fail("Error", false, _clock);
        var exFailed = Assert.Throws<InvalidOperationException>(() => failedStep.Complete(null, _clock));
        Assert.Contains("Only Running step executions can be completed", exFailed.Message);

        var cancelledStep = CreatePendingStep();
        cancelledStep.Cancel(_clock);
        var exCancelled = Assert.Throws<InvalidOperationException>(() => cancelledStep.Complete(null, _clock));
        Assert.Contains("Only Running step executions can be completed", exCancelled.Message);

        var skippedStep = CreatePendingStep();
        skippedStep.Skip(_clock);
        var exSkipped = Assert.Throws<InvalidOperationException>(() => skippedStep.Complete(null, _clock));
        Assert.Contains("Only Running step executions can be completed", exSkipped.Message);
    }

    // =========================================================================
    // 5. Fail Transition
    // =========================================================================

    [Fact]
    public void Fail_transitions_from_running_to_failed_and_sets_error_and_retryability()
    {
        var step = CreateRunningStep();
        _clock.Advance(TimeSpan.FromMinutes(2));
        var failTime = _clock.UtcNow;

        step.Fail("  Downstream API timeout  ", isRetryable: true, _clock);

        Assert.Equal(WorkflowStepExecutionStatus.Failed, step.Status);
        Assert.Equal("Downstream API timeout", step.ErrorMessage);
        Assert.True(step.IsRetryable);
        Assert.Equal(failTime, step.CompletedAt);
        Assert.Equal(failTime, step.FinishedAt);
        Assert.Equal(failTime, step.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fail_rejects_null_empty_or_whitespace_error_message(string? invalidError)
    {
        var step = CreateRunningStep();

        Assert.ThrowsAny<ArgumentException>(() => step.Fail(invalidError!, isRetryable: false, _clock));
    }

    [Fact]
    public void Fail_reads_clock_exactly_once()
    {
        var step = CreateRunningStep();
        var readsBefore = _clock.ReadsCount;

        step.Fail("Error", isRetryable: false, _clock);

        Assert.Equal(readsBefore + 1, _clock.ReadsCount);
    }

    [Fact]
    public void Fail_rejects_null_clock()
    {
        var step = CreateRunningStep();

        Assert.Throws<ArgumentNullException>(() => step.Fail("Error", isRetryable: false, null!));
    }

    [Fact]
    public void Fail_rejects_non_running_step_executions()
    {
        var pendingStep = CreatePendingStep();
        var exPending = Assert.Throws<InvalidOperationException>(() => pendingStep.Fail("Error", false, _clock));
        Assert.Contains("Only Running step executions can be failed", exPending.Message);

        var completedStep = CreateRunningStep();
        completedStep.Complete(null, _clock);
        var exCompleted = Assert.Throws<InvalidOperationException>(() => completedStep.Fail("Error", false, _clock));
        Assert.Contains("Only Running step executions can be failed", exCompleted.Message);

        var failedStep = CreateRunningStep();
        failedStep.Fail("Error", false, _clock);
        var exFailed = Assert.Throws<InvalidOperationException>(() => failedStep.Fail("Error", false, _clock));
        Assert.Contains("Only Running step executions can be failed", exFailed.Message);

        var cancelledStep = CreatePendingStep();
        cancelledStep.Cancel(_clock);
        var exCancelled = Assert.Throws<InvalidOperationException>(() => cancelledStep.Fail("Error", false, _clock));
        Assert.Contains("Only Running step executions can be failed", exCancelled.Message);

        var skippedStep = CreatePendingStep();
        skippedStep.Skip(_clock);
        var exSkipped = Assert.Throws<InvalidOperationException>(() => skippedStep.Fail("Error", false, _clock));
        Assert.Contains("Only Running step executions can be failed", exSkipped.Message);
    }

    // =========================================================================
    // 6. Cancel Transition
    // =========================================================================

    [Fact]
    public void Cancel_transitions_from_pending_to_cancelled()
    {
        var step = CreatePendingStep();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var cancelTime = _clock.UtcNow;

        step.Cancel(_clock);

        Assert.Equal(WorkflowStepExecutionStatus.Cancelled, step.Status);
        Assert.Null(step.StartedAt);
        Assert.Null(step.CompletedAt);
        Assert.Equal(cancelTime, step.CancelledAt);
        Assert.Equal(cancelTime, step.FinishedAt);
        Assert.Equal(cancelTime, step.UpdatedAt);
    }

    [Fact]
    public void Cancel_transitions_from_running_to_cancelled_and_preserves_started_at()
    {
        var step = CreatePendingStep();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var startTime = _clock.UtcNow;
        step.Start(_clock);

        _clock.Advance(TimeSpan.FromMinutes(2));
        var cancelTime = _clock.UtcNow;
        step.Cancel(_clock);

        Assert.Equal(WorkflowStepExecutionStatus.Cancelled, step.Status);
        Assert.Equal(startTime, step.StartedAt);
        Assert.Null(step.CompletedAt);
        Assert.Equal(cancelTime, step.CancelledAt);
        Assert.Equal(cancelTime, step.FinishedAt);
        Assert.Equal(cancelTime, step.UpdatedAt);
    }

    [Fact]
    public void Cancel_reads_clock_exactly_once()
    {
        var step = CreatePendingStep();
        var readsBefore = _clock.ReadsCount;

        step.Cancel(_clock);

        Assert.Equal(readsBefore + 1, _clock.ReadsCount);
    }

    [Fact]
    public void Cancel_rejects_null_clock()
    {
        var step = CreatePendingStep();

        Assert.Throws<ArgumentNullException>(() => step.Cancel(null!));
    }

    [Fact]
    public void Cancel_rejects_terminal_step_executions()
    {
        var completedStep = CreateRunningStep();
        completedStep.Complete(null, _clock);
        var exCompleted = Assert.Throws<InvalidOperationException>(() => completedStep.Cancel(_clock));
        Assert.Contains("Only Pending or Running step executions can be cancelled", exCompleted.Message);

        var failedStep = CreateRunningStep();
        failedStep.Fail("Error", false, _clock);
        var exFailed = Assert.Throws<InvalidOperationException>(() => failedStep.Cancel(_clock));
        Assert.Contains("Only Pending or Running step executions can be cancelled", exFailed.Message);

        var cancelledStep = CreatePendingStep();
        cancelledStep.Cancel(_clock);
        var exCancelled = Assert.Throws<InvalidOperationException>(() => cancelledStep.Cancel(_clock));
        Assert.Contains("Only Pending or Running step executions can be cancelled", exCancelled.Message);

        var skippedStep = CreatePendingStep();
        skippedStep.Skip(_clock);
        var exSkipped = Assert.Throws<InvalidOperationException>(() => skippedStep.Cancel(_clock));
        Assert.Contains("Only Pending or Running step executions can be cancelled", exSkipped.Message);
    }

    // =========================================================================
    // 7. Skip Transition
    // =========================================================================

    [Fact]
    public void Skip_transitions_from_pending_to_skipped_and_sets_completed_at()
    {
        var step = CreatePendingStep();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var skipTime = _clock.UtcNow;

        step.Skip(_clock);

        Assert.Equal(WorkflowStepExecutionStatus.Skipped, step.Status);
        Assert.Null(step.StartedAt);
        Assert.Null(step.CancelledAt);
        Assert.Null(step.ErrorMessage);
        Assert.Equal(skipTime, step.CompletedAt);
        Assert.Equal(skipTime, step.FinishedAt);
        Assert.Equal(skipTime, step.UpdatedAt);
    }

    [Fact]
    public void Skip_reads_clock_exactly_once()
    {
        var step = CreatePendingStep();
        var readsBefore = _clock.ReadsCount;

        step.Skip(_clock);

        Assert.Equal(readsBefore + 1, _clock.ReadsCount);
    }

    [Fact]
    public void Skip_rejects_null_clock()
    {
        var step = CreatePendingStep();

        Assert.Throws<ArgumentNullException>(() => step.Skip(null!));
    }

    [Fact]
    public void Skip_rejects_non_pending_step_executions()
    {
        var runningStep = CreateRunningStep();
        var exRunning = Assert.Throws<InvalidOperationException>(() => runningStep.Skip(_clock));
        Assert.Contains("Only Pending step executions can be skipped", exRunning.Message);

        var completedStep = CreateRunningStep();
        completedStep.Complete(null, _clock);
        var exCompleted = Assert.Throws<InvalidOperationException>(() => completedStep.Skip(_clock));
        Assert.Contains("Only Pending step executions can be skipped", exCompleted.Message);

        var failedStep = CreateRunningStep();
        failedStep.Fail("Error", false, _clock);
        var exFailed = Assert.Throws<InvalidOperationException>(() => failedStep.Skip(_clock));
        Assert.Contains("Only Pending step executions can be skipped", exFailed.Message);

        var cancelledStep = CreatePendingStep();
        cancelledStep.Cancel(_clock);
        var exCancelled = Assert.Throws<InvalidOperationException>(() => cancelledStep.Skip(_clock));
        Assert.Contains("Only Pending step executions can be skipped", exCancelled.Message);

        var skippedStep = CreatePendingStep();
        skippedStep.Skip(_clock);
        var exSkipped = Assert.Throws<InvalidOperationException>(() => skippedStep.Skip(_clock));
        Assert.Contains("Only Pending step executions can be skipped", exSkipped.Message);
    }

    // =========================================================================
    // 8. Terminal Immutability
    // =========================================================================

    [Theory]
    [InlineData(WorkflowStepExecutionStatus.Completed)]
    [InlineData(WorkflowStepExecutionStatus.Failed)]
    [InlineData(WorkflowStepExecutionStatus.Cancelled)]
    [InlineData(WorkflowStepExecutionStatus.Skipped)]
    public void Terminal_step_executions_reject_all_mutations_and_preserve_state(WorkflowStepExecutionStatus terminalStatus)
    {
        var step = CreatePendingStep();
        switch (terminalStatus)
        {
            case WorkflowStepExecutionStatus.Completed:
                step.Start(_clock);
                step.Complete("{\"ok\":true}", _clock);
                break;
            case WorkflowStepExecutionStatus.Failed:
                step.Start(_clock);
                step.Fail("Terminal error", isRetryable: false, _clock);
                break;
            case WorkflowStepExecutionStatus.Cancelled:
                step.Cancel(_clock);
                break;
            case WorkflowStepExecutionStatus.Skipped:
                step.Skip(_clock);
                break;
        }

        var statusBefore = step.Status;
        var startedAtBefore = step.StartedAt;
        var completedAtBefore = step.CompletedAt;
        var cancelledAtBefore = step.CancelledAt;
        var finishedAtBefore = step.FinishedAt;
        var updatedAtBefore = step.UpdatedAt;
        var errorMessageBefore = step.ErrorMessage;
        var isRetryableBefore = step.IsRetryable;
        var outputMetadataBefore = step.OutputMetadata;

        Assert.Throws<InvalidOperationException>(() => step.Start(_clock));
        Assert.Throws<InvalidOperationException>(() => step.Complete("{}", _clock));
        Assert.Throws<InvalidOperationException>(() => step.Fail("Error", false, _clock));
        Assert.Throws<InvalidOperationException>(() => step.Cancel(_clock));
        Assert.Throws<InvalidOperationException>(() => step.Skip(_clock));

        Assert.Equal(statusBefore, step.Status);
        Assert.Equal(startedAtBefore, step.StartedAt);
        Assert.Equal(completedAtBefore, step.CompletedAt);
        Assert.Equal(cancelledAtBefore, step.CancelledAt);
        Assert.Equal(finishedAtBefore, step.FinishedAt);
        Assert.Equal(updatedAtBefore, step.UpdatedAt);
        Assert.Equal(errorMessageBefore, step.ErrorMessage);
        Assert.Equal(isRetryableBefore, step.IsRetryable);
        Assert.Equal(outputMetadataBefore, step.OutputMetadata);
    }
}
