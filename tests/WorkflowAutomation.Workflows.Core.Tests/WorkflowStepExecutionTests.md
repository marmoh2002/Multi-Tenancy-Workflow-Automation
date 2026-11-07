# Workflow Step Execution Entity Tests

Unit tests for `WorkflowStepExecution` and `WorkflowExecution.CreateStepExecution` in `WorkflowAutomation.Workflows.Core.Entities`. They verify aggregate root child creation invariants, step attempt number validation, definition consistency checks, state machine transitions, single clock reads, and terminal state immutability.

---

## Test Infrastructure

| Component | Purpose |
| :--- | :--- |
| `TestClock` | Deterministic `IClock` implementation. Supports initial time, manual `Advance(TimeSpan)`, and a `ReadsCount` counter to verify exact single clock reads per transition. |
| `WorkspaceId`, `CreatorId` | Fixed static `Guid` values shared across tests. |
| `CreateTestWorkflow(...)` | Creates an active test `Workflow` aggregate with valid name and description. |
| `CreatePublishedVersion(...)` | Creates and publishes a valid 2-step (`step-1`, `step-2`) DAG `WorkflowVersion`, providing a published target for execution pinning. |
| `CreatePendingExecution(...)` | Factory helper returning a newly instantiated `WorkflowExecution` in `Pending` status. |
| `CreateRunningExecution()` | Factory helper returning an execution started into `Running` status. |
| `CreatePendingStep(...)` | Factory helper returning a `WorkflowStepExecution` created via `execution.CreateStepExecution(...)` in `Pending` status. |
| `CreateRunningStep(...)` | Factory helper returning a `WorkflowStepExecution` transitioned into `Running` status. |

---

## Coverage Summary

### 1. Creation & Aggregate Invariants (`WorkflowExecution.CreateStepExecution`)

| Test | Verified Behavior |
| :--- | :--- |
| `CreateStepExecution_initializes_pending_step_execution_with_correct_fields` | Verifies that the child step attempt inherits `WorkspaceId` and `ExecutionId` from the parent execution; sets `Status = Pending`, `AttemptNumber`, `StepId`, `IsRetryable = false`, `CreatedAt = now`, `UpdatedAt = null`; and ensures all completion/cancellation timestamps and error metadata are initialized to `null`. |
| `CreateStepExecution_normalizes_and_trims_step_id` | Verifies that whitespace-padded step identifiers (e.g. `"  step-1  "`) are trimmed during normalization before step existence checks. |
| `CreateStepExecution_succeeds_when_execution_is_pending_or_running` | Theory verifying that new step execution attempts can be created when the parent execution is in either `Pending` or `Running` status. |

### 2. Creation Guards

| Test | Verified Behavior |
| :--- | :--- |
| `CreateStepExecution_rejects_empty_id` | Throws `ArgumentException` when step execution ID is `Guid.Empty`. |
| `CreateStepExecution_rejects_nonexistent_step_in_definition` | Throws `InvalidOperationException` if the targeted step identifier does not exist in the parent execution's pinned `WorkflowDefinition`. |
| `CreateStepExecution_rejects_null_empty_or_whitespace_step_id` | Theory verifying that `null`, empty string, and whitespace-only step IDs throw `ArgumentException` (or `ArgumentNullException`). |
| `CreateStepExecution_rejects_step_id_exceeding_max_length` | Throws `ArgumentException` when `StepId` exceeds 100 characters (`StepDefinition.MaxIdLength`). |
| `CreateStepExecution_rejects_non_positive_attempt_number` | Theory verifying that non-positive attempt numbers (0, -1, -42) throw `ArgumentOutOfRangeException`. |
| `CreateStepExecution_rejects_null_clock` | Throws `ArgumentNullException` when `clock` is `null`. |
| `CreateStepExecution_rejects_completed_execution` | Enforces aggregate boundary: throws `InvalidOperationException` when attempting to create a step attempt on a parent execution that is already `Completed`. |
| `CreateStepExecution_rejects_failed_execution` | Enforces aggregate boundary: throws `InvalidOperationException` when attempting to create a step attempt on a parent execution that is already `Failed`. |
| `CreateStepExecution_rejects_cancelled_execution` | Enforces aggregate boundary: throws `InvalidOperationException` when attempting to create a step attempt on a parent execution that is already `Cancelled`. |
| `CreateStepExecution_rejects_pending_execution_with_cancellation_request` | Enforces aggregate boundary: throws `InvalidOperationException` when attempting to create a step attempt on a `Pending` parent execution that has a recorded cancellation request. |
| `CreateStepExecution_rejects_running_execution_with_cancellation_request` | Enforces aggregate boundary: throws `InvalidOperationException` when attempting to create a step attempt on a `Running` parent execution that has a recorded cancellation request. |
| `CreateStepExecution_rejects_tampered_snapshot` | Verifies definition snapshot integrity check before parsing: throws `InvalidOperationException` if `HasIntactSnapshot()` fails. |

### 3. Start Transition (`Start`)

| Test | Verified Behavior |
| :--- | :--- |
| `Start_transitions_from_pending_to_running_and_sets_timestamps` | Transitions status from `Pending` to `Running`; records `StartedAt = now` and `UpdatedAt = now`. |
| `Start_reads_clock_exactly_once` | Verifies that exactly one deterministic clock read occurs during the transition using `TestClock.ReadsCount`. |
| `Start_rejects_null_clock` | Throws `ArgumentNullException` when `clock` is `null`. |
| `Start_rejects_non_pending_step_executions` | Verifies `InvalidOperationException` when called on steps in `Running`, `Completed`, `Failed`, `Cancelled`, or `Skipped` status. |

### 4. Complete Transition (`Complete`)

| Test | Verified Behavior |
| :--- | :--- |
| `Complete_transitions_from_running_to_completed_with_output_metadata` | Transitions status from `Running` to `Completed`; sets `CompletedAt = now`, `FinishedAt = now`, `OutputMetadata`, and `UpdatedAt = now`. |
| `Complete_accepts_null_output_metadata` | Verifies that steps completing without output metadata record `OutputMetadata = null` cleanly. |
| `Complete_reads_clock_exactly_once` | Verifies that exactly one deterministic clock read occurs during completion. |
| `Complete_rejects_null_clock` | Throws `ArgumentNullException` when `clock` is `null`. |
| `Complete_rejects_non_running_step_executions` | Verifies `InvalidOperationException` when called on steps in `Pending`, `Completed`, `Failed`, `Cancelled`, or `Skipped` status. |

### 5. Fail Transition (`Fail`)

| Test | Verified Behavior |
| :--- | :--- |
| `Fail_transitions_from_running_to_failed_and_sets_error_and_retryability` | Transitions status from `Running` to `Failed`; records trimmed `ErrorMessage`, `IsRetryable` flag, `CompletedAt = now`, `FinishedAt = now`, and `UpdatedAt = now`. |
| `Fail_rejects_null_empty_or_whitespace_error_message` | Theory verifying that `null`, empty string, and whitespace-only error messages throw `ArgumentException` (or `ArgumentNullException`). |
| `Fail_reads_clock_exactly_once` | Verifies that exactly one deterministic clock read occurs during failure. |
| `Fail_rejects_null_clock` | Throws `ArgumentNullException` when `clock` is `null`. |
| `Fail_rejects_non_running_step_executions` | Verifies `InvalidOperationException` when called on steps in `Pending`, `Completed`, `Failed`, `Cancelled`, or `Skipped` status. |

### 6. Cancel Transition (`Cancel`)

| Test | Verified Behavior |
| :--- | :--- |
| `Cancel_transitions_from_pending_to_cancelled` | Transitions status from `Pending` to `Cancelled`; records `CancelledAt = now`, `FinishedAt = now`, `UpdatedAt = now`, while leaving `StartedAt = null` and `CompletedAt = null`. |
| `Cancel_transitions_from_running_to_cancelled_and_preserves_started_at` | Transitions status from `Running` to `Cancelled`; records `CancelledAt = now`, `FinishedAt = now`, `UpdatedAt = now`, while strictly preserving the initial `StartedAt` timestamp. |
| `Cancel_reads_clock_exactly_once` | Verifies that exactly one deterministic clock read occurs during cancellation. |
| `Cancel_rejects_null_clock` | Throws `ArgumentNullException` when `clock` is `null`. |
| `Cancel_rejects_terminal_step_executions` | Verifies `InvalidOperationException` when called on steps already in terminal status (`Completed`, `Failed`, `Cancelled`, `Skipped`). |

### 7. Skip Transition (`Skip`)

| Test | Verified Behavior |
| :--- | :--- |
| `Skip_transitions_from_pending_to_skipped_and_sets_completed_at` | Transitions status from `Pending` to `Skipped` (fail-fast / upstream failure); records `CompletedAt = now`, `FinishedAt = now`, `UpdatedAt = now`, while leaving `StartedAt = null`, `CancelledAt = null`, and `ErrorMessage = null`. |
| `Skip_reads_clock_exactly_once` | Verifies that exactly one deterministic clock read occurs during skipping. |
| `Skip_rejects_null_clock` | Throws `ArgumentNullException` when `clock` is `null`. |
| `Skip_rejects_non_pending_step_executions` | Verifies `InvalidOperationException` when called on steps in `Running`, `Completed`, `Failed`, `Cancelled`, or `Skipped` status. |

### 8. Terminal Immutability

| Test | Verified Behavior |
| :--- | :--- |
| `Terminal_step_executions_reject_all_mutations_and_preserve_state` | Comprehensive Theory across all four terminal states (`Completed`, `Failed`, `Cancelled`, `Skipped`). Verifies that calling any subsequent transition (`Start`, `Complete`, `Fail`, `Cancel`, `Skip`) throws `InvalidOperationException`, and all properties (`Status`, `StartedAt`, `CompletedAt`, `CancelledAt`, `FinishedAt`, `UpdatedAt`, `ErrorMessage`, `IsRetryable`, `OutputMetadata`) remain completely unmodified. |

