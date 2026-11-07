# WorkflowStepExecutionPersistenceTests

Integration tests for persisting `WorkflowStepExecution` rows in PostgreSQL, covering lifecycle round-trips, per-attempt uniqueness, tenant isolation, composite foreign-key enforcement, and cascade deletion with the parent execution.

## Setup

- Uses `PostgreSqlFixture` (Testcontainers `postgres:15-alpine`) with the real EF Core migrations and production DI via `AddInfrastructurePersistence`.
- Each test uses explicit scopes from `_fixture.CreateScope()`.
- `WorkspaceContext.SetContext(workspaceId, userId)` establishes the active tenant before every query.
- `SeedExecutionAsync` seeds a user, workspace, workflow, published version, and running `WorkflowExecution` through the domain.
- Step attempts are created through the parent aggregate with `execution.CreateStepExecution(...)`, transitioned with domain methods, and persisted with `SaveChangesAsync()`.

## Test Cases

| Test | Verifies |
| --- | --- |
| `GivenPendingStep_WhenSavedAndReloaded_RoundTripsAllProperties` | Reloads a freshly created attempt in a fresh scope and verifies identity, workspace/execution ownership, `StepId`, `AttemptNumber`, `Pending` status, all nullable timestamps and fields, `CreatedAt`, and `UpdatedAt`. |
| `GivenCompletedStepWithMetadata_WhenSavedAndReloaded_RoundTripsSuccessfully` | Starts and completes an attempt with output metadata. Verifies `Completed` status, `StartedAt`, `CompletedAt`, computed `FinishedAt`, `OutputMetadata`, and that no error or retryable flag is persisted. |
| `GivenFailedStepWithRetryable_WhenSavedAndReloaded_RoundTripsErrorMessageAndFlag` | Fails an attempt with an untrimmed message and `isRetryable: true`. Verifies the message is stored trimmed, the flag survives, and terminal timestamps round-trip. |
| `GivenCancelledStep_WhenSavedAndReloaded_RoundTripsTimestamps` | Cancels a running attempt. Verifies `StartedAt` is preserved, `CancelledAt` is set, `CompletedAt` stays null, and `FinishedAt` falls back to `CancelledAt`. |
| `GivenSkippedStep_WhenSavedAndReloaded_RoundTripsTimestamps` | Skips a pending attempt without starting it. Verifies `StartedAt` stays null, `CompletedAt` is set, and `FinishedAt` falls back to `CompletedAt`. |
| `GivenDuplicateAttemptNumber_WhenSaved_ThrowsDbUpdateException` | Inserts a second row with the same `(ExecutionId, StepId, AttemptNumber)`. Verifies SQLSTATE `23505` and the unique index `IX_WorkflowStepExecutions_ExecutionId_StepId_AttemptNumber`, then confirms only the first attempt exists. |
| `GivenStepExecutionsInDifferentWorkspaces_WhenQueried_GlobalQueryFilterIsolatesTenant` | Seeds one attempt per tenant. Each workspace sees only its own row; a query without an active workspace context fails closed; `IgnoreQueryFilters()` confirms both rows physically exist. |
| `GivenCrossWorkspaceWrite_WhenSaved_TenantWriteValidationInterceptorThrows` | Tracks a step attempt for another workspace's execution. Verifies `TenantWriteValidationInterceptor` rejects it with `InvalidOperationException` and that the row was never persisted. |
| `GivenMismatchedWorkspaceId_WhenRawInserted_CompositeForeignKeyRejects` | Raw-inserts an attempt claiming Workspace B while referencing an execution owned by Workspace A. The single-column workspace FK passes; the composite FK `FK_WorkflowStepExecutions_Executions_WorkspaceId_ExecutionId` rejects it with SQLSTATE `23503`. |
| `GivenExecutionWithSteps_WhenExecutionDeleted_CascadesStepAttempts` | Persists three attempts (including a retry) and deletes the parent execution. Verifies all attempts cascade away while the workflow and version remain. |

## Key Behaviors

- `WorkflowStepExecutions` is tenant-scoped through the `WorkspaceId` global query filter.
- The composite FK `(WorkspaceId, ExecutionId) -> WorkflowExecutions(WorkspaceId, Id)` prevents cross-workspace execution references even for raw SQL inserts.
- `ix_WorkflowStepExecutions_ExecutionId_StepId_AttemptNumber` is unique, so retries must increment `AttemptNumber`.
- Step attempts have `OnDelete(DeleteBehavior.Cascade)` relative to their parent execution, so they never outlive it.
- Workflow deletion does not delete step attempts directly. Because terminal execution history is retained, attempts attached to retained executions also survive; this is asserted in `WorkflowExecutionPersistenceTests`.
- `FinishedAt` is a computed domain property (`CompletedAt ?? CancelledAt`) and is intentionally ignored by EF Core mapping.

## Execution

```bash
dotnet test tests/WorkflowAutomation.Integration.Tests/ --filter FullyQualifiedName~WorkflowStepExecutionPersistenceTests
```
