# Workflow Execution Entity Tests

Unit tests for the `WorkflowExecution` entity in `WorkflowAutomation.Workflows.Core.Entities`. They verify creation invariants, immutable version snapshots, state machine transitions, message idempotency, race condition resolutions, and "Point of No Return" guards.

---

## Test Infrastructure

| Component | Purpose |
| :--- | :--- |
| `TestClock` | Deterministic `IClock` implementation. Supports initial time, manual `Advance(TimeSpan)`, a `ReadsCount` counter, and an optional `advancePerRead` configuration that advances time on each read to mathematically prove single clock reads. |
| `WorkspaceId`, `CreatorId` | Fixed static `Guid` values shared across tests. |
| `CreateTestWorkflow(...)` | Creates an active test `Workflow` aggregate with valid name and description. |
| `CreatePublishedVersion(...)` | Creates and publishes a valid 1-step DAG `WorkflowVersion`, providing a published target for execution pinning. |
| `CreatePendingExecution(...)` | Factory helper returning a newly instantiated `WorkflowExecution` in `Pending` status. |

---

## Coverage Summary

### 1. Creation & Snapshot Invariants (`WorkflowExecution.Create`)

| Test | Verified Behavior |
| :--- | :--- |
| `Create_initializes_pending_execution_with_immutable_snapshot` | Verifies that all identifiers, names, version details, and serialized definitions are pinned; verifies lowercase 64-char hexadecimal SHA-256 pattern `^[0-9a-f]{64}$`; sets `Status = Pending`, `IsCancellable = true`, `CreatedAt = now`, `UpdatedAt = null`; and ensures `HasIntactSnapshot()` returns `true`. |
| `Create_scheduled_execution_persists_schedule_identifiers` | Verifies positive scheduled creation path: `Source = Scheduled`, trims `ScheduledOccurrenceId`, and stores `ScheduleId`. |
| `Create_rejects_empty_id` | Throws `ArgumentException` when `id` is `Guid.Empty`. |
| `Create_rejects_unpublished_version` | Throws `InvalidOperationException` if the targeted version is still in draft state. |
| `Create_rejects_version_belonging_to_different_workflow` | Throws `InvalidOperationException` if `version.WorkflowId != workflow.Id`. |
| `Create_rejects_version_belonging_to_different_workspace` | Throws `InvalidOperationException` if `version.WorkspaceId != workflow.WorkspaceId`. |
| `Create_rejects_undefined_source` | Throws `ArgumentOutOfRangeException` when passed an undefined enum value (e.g. `(WorkflowExecutionSource)999`). |
| `Create_rejects_empty_guid_identifiers` | Throws `ArgumentException` if `requestedByUserId` or `scheduleId` is passed as `Guid.Empty`. |
| `Create_scheduled_execution_requires_schedule_and_occurrence_id` | Throws `ArgumentException` if a `Scheduled` execution lacks a `ScheduleId` or a non-whitespace `ScheduledOccurrenceId`. |
| `Create_scheduled_execution_rejects_overlong_occurrence_id` | Throws `ArgumentException` if `ScheduledOccurrenceId` exceeds the 200-character column limit. |
| `Create_manual_or_webhook_rejects_schedule_parameters` | Throws `ArgumentException` if a `Manual` or `Webhook` execution is passed a `ScheduleId` or `ScheduledOccurrenceId`. |

### 2. Start Transition (`Start`)

| Test | Verified Behavior |
| :--- | :--- |
| `Start_transitions_pending_to_running_and_sets_timestamps` | Transitions status from `Pending` to `Running`. Uses an auto-advancing `TestClock` (`advancePerRead: 1s`) to mathematically prove a single clock read occurred: `StartedAt` equals `UpdatedAt`, and `ReadsCount` increments by exactly 1. |
| `Start_rejects_execution_with_pending_cancellation_request` | Throws `InvalidOperationException` if a cancellation request was recorded before starting. |
| `Start_rejects_all_non_pending_statuses` | Systematically asserts `InvalidOperationException` when called on any execution not in `Pending` status (`Running`, `Completed`, `Failed`, or `Cancelled`). |

### 3. Complete Transition & Race Precedence (`Complete`)

| Test | Verified Behavior |
| :--- | :--- |
| `Complete_transitions_running_to_completed` | Transitions status from `Running` to `Completed`; sets `CompletedAt = now`, `FinishedAt = now`, and `UpdatedAt = now`. |
| `Complete_clears_pending_cancellation_when_completion_wins_race` | Resolves the concurrency race between worker completion and user cancellation ("Completion Wins"). If cancellation was pending, completing clears `CancellationRequestedAt` and `CancellationReason`, ensuring the terminal state is clean and unambiguous. |
| `Complete_rejects_all_non_running_statuses` | Systematically tests and asserts `InvalidOperationException` when called on executions in `Pending`, `Completed`, `Failed`, or `Cancelled` status. |

### 4. Fail Transition & Dispatch Failures (`Fail`)

| Test | Verified Behavior |
| :--- | :--- |
| `Fail_from_running_transitions_to_failed_and_trims_inputs` | Transitions `Running` to `Failed`; trims `FailedStepId` and `FailureReason`; sets `CompletedAt = now` and `FinishedAt = now`. |
| `Fail_from_pending_allows_dispatch_or_startup_failures` | Allows executions to fail directly from `Pending` (for dispatch, queue, or allocation failures) without requiring a synthetic `Start()`. `FailedStepId` is allowed to be `null`. |
| `Fail_normalizes_empty_failed_step_id_to_null` | Normalizes empty or whitespace-only `failedStepId` inputs to `null`. |
| `Fail_rejects_overlong_failed_step_id` | Throws `ArgumentException` if `FailedStepId` exceeds the 100-character limit. |
| `Fail_clears_pending_cancellation_when_failure_wins_race` | If a permanent failure occurs while cancellation is pending, failure wins the race and resets `CancellationRequestedAt` and `CancellationReason`. |
| `Fail_rejects_missing_or_whitespace_reason` | Theory testing `null`, `""`, and `"   "`; all throw `ArgumentException`. |
| `Fail_rejects_all_terminal_statuses` | Systematically asserts `InvalidOperationException` when attempting to fail executions that are already `Completed`, `Failed`, or `Cancelled`. |

### 5. Cancellation Idempotency & Point of No Return (`RequestCancellation`, `PreventCancellation`, `MarkCancelled`)

| Test | Verified Behavior |
| :--- | :--- |
| `RequestCancellation_records_request_on_active_execution` | Records `CancellationRequestedAt = now` and trimmed `CancellationReason` on active executions while preserving status. |
| `RequestCancellation_is_idempotent_on_retries_and_preserves_initial_data` | Safe at-least-once message retry handling: duplicate cancellation calls return as a clean no-op, preserving the initial request timestamp and reason without throwing validation exceptions. |
| `RequestCancellation_idempotent_retry_leaves_updated_at_unchanged` | Verifies that idempotent retries do not mutate `UpdatedAt`, adhering to the domain rule that no-op operations leave audit timestamps untouched. |
| `RequestCancellation_is_noop_if_already_cancelled` | If an execution is already `Cancelled`, a subsequent cancellation request returns cleanly as a no-op instead of throwing an invalid terminal state exception. |
| `RequestCancellation_rejects_all_terminal_statuses` | Asserts `InvalidOperationException` when attempting to cancel executions that reached `Completed` or `Failed`. |
| `RequestCancellation_rejects_missing_or_whitespace_reason_on_first_request` | Theory testing `null`, `""`, and `"   "`; all throw `ArgumentException` when cancellation has not yet been recorded. |
| `PreventCancellation_locks_execution_against_cancellation_and_updates_timestamp` | Sets `IsCancellable = false` and updates `UpdatedAt = now`. Any subsequent call to `RequestCancellation` throws `InvalidOperationException` indicating the point of no return has been passed. |
| `PreventCancellation_rejects_all_non_running_or_already_requested_executions` | Systematically asserts `InvalidOperationException` if invoked on non-running executions (`Pending`, `Completed`, `Failed`, `Cancelled`) or after cancellation has already been recorded. |
| `MarkCancelled_transitions_to_cancelled_with_timestamps` | Transitions status to `Cancelled`; sets `CancelledAt = now`, `FinishedAt = now`, and `UpdatedAt = now`. |
| `MarkCancelled_requires_prior_cancellation_request` | Throws `InvalidOperationException` if invoked without an existing recorded cancellation request. |

### 6. Snapshot Integrity & Immutability Across Transitions

| Test | Verified Behavior |
| :--- | :--- |
| `Snapshot_fields_remain_immutable_across_lifecycle_transitions` | Verifies that `WorkflowName`, `VersionNumber`, `DefinitionSchemaVersion`, `DefinitionSnapshot`, and `DefinitionHash` remain strictly unchanged across multiple transitions (`Start` -> `PreventCancellation` -> `Complete`). |
| `HasIntactSnapshot_and_GetDefinition_succeed_for_valid_snapshot` | Verifies that `HasIntactSnapshot()` returns `true` for unmodified serialized definitions and that `GetDefinition()` successfully deserializes the snapshot back into a strongly-typed `WorkflowDefinition`. |
