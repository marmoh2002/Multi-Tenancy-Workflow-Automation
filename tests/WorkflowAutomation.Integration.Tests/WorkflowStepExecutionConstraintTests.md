# WorkflowStepExecutionConstraintTests

Integration tests that bypass EF Core and the domain with raw SQL inserts to verify that PostgreSQL itself rejects invalid `WorkflowStepExecutions` rows. The suite covers all 13 check constraints defined on the table.

## Setup

- Uses `PostgreSqlFixture` (Testcontainers `postgres:15-alpine`) with the real EF Core migrations.
- Each test seeds a valid `ApplicationUser` and `Workspace` through the domain so both workspace foreign keys are satisfied.
- `SeedExecutionAsync` raw-inserts a valid `Pending` `WorkflowExecution`, providing the `(WorkspaceId, ExecutionId)` pair required by the composite foreign key.
- `InsertInvalidStepExecutionAsync` executes a parameterized `INSERT` with a complete valid step-execution row, overriding only the field(s) under test.
- A valid trimmed step ID, positive attempt number, `Pending` status, null timestamps, and null conditional fields are the defaults.
- Failures are asserted as `PostgresException` with SQLSTATE `23514` (`check_violation`) and the expected `ConstraintName`.

## Test Cases

| Test | Invalid input | Expected PostgreSQL rejection |
| --- | --- | --- |
| `GivenUntrimmedStepId_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `StepId = "  step-1  "` | `CK_WorkflowStepExecutions_StepId_TrimmedAndNotEmpty` |
| `GivenEmptyStepId_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `StepId = ""` | `CK_WorkflowStepExecutions_StepId_TrimmedAndNotEmpty` |
| `GivenZeroAttemptNumber_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `AttemptNumber = 0` | `CK_WorkflowStepExecutions_AttemptNumber_Positive` |
| `GivenNegativeAttemptNumber_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `AttemptNumber = -1` | `CK_WorkflowStepExecutions_AttemptNumber_Positive` |
| `GivenInvalidStatus_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Status = 99` | `CK_WorkflowStepExecutions_Status_Enum_Valid` |
| `GivenPendingStepWithStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Pending`, `StartedAt` set | `CK_WorkflowStepExecutions_Pending_NoTimestamps` |
| `GivenPendingStepWithCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Pending`, `CompletedAt` set | `CK_WorkflowStepExecutions_Pending_NoTimestamps` |
| `GivenPendingStepWithCancelledAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Pending`, `CancelledAt` set | `CK_WorkflowStepExecutions_Pending_NoTimestamps` |
| `GivenRunningStepWithoutStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Running`, `StartedAt = null` | `CK_WorkflowStepExecutions_Running_RequiresStartedAt` |
| `GivenRunningStepWithCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Running`, `StartedAt` and `CompletedAt` set | `CK_WorkflowStepExecutions_Running_RequiresStartedAt` |
| `GivenCompletedStepWithoutStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Completed`, `StartedAt = null`, `CompletedAt` set | `CK_WorkflowStepExecutions_Completed_RequiresTimestamps` |
| `GivenCompletedStepWithoutCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Completed`, `StartedAt` set, `CompletedAt = null` | `CK_WorkflowStepExecutions_Completed_RequiresTimestamps` |
| `GivenFailedStepWithoutErrorMessage_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Failed`, both timestamps, `ErrorMessage = null` | `CK_WorkflowStepExecutions_Failed_RequiresTimestampsAndReason` |
| `GivenFailedStepWithWhitespaceErrorMessage_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Failed`, both timestamps, `ErrorMessage = "   "` | `CK_WorkflowStepExecutions_Failed_RequiresTimestampsAndReason` |
| `GivenCancelledStepWithoutCancelledAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Cancelled`, `CancelledAt = null` | `CK_WorkflowStepExecutions_Cancelled_RequiresCancelledAt` |
| `GivenSkippedStepWithStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Skipped`, `StartedAt` and `CompletedAt` set | `CK_WorkflowStepExecutions_Skipped_RequiresCompletedAt` |
| `GivenSkippedStepWithoutCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Skipped`, both timestamps null | `CK_WorkflowStepExecutions_Skipped_RequiresCompletedAt` |
| `GivenCompletedStepWithCancelledAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Completed`, both timestamps, `CancelledAt` set | `CK_WorkflowStepExecutions_FinishTimestamps_Exclusive` |
| `GivenErrorMessageOnCompletedStep_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Completed`, both timestamps, `ErrorMessage = "Err"` | `CK_WorkflowStepExecutions_ErrorMessage_OnlyOnFailed` |
| `GivenOutputMetadataOnFailedStep_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Failed`, both timestamps, `ErrorMessage = "Err"`, `OutputMetadata = "{}"` | `CK_WorkflowStepExecutions_OutputMetadata_OnlyOnCompleted` |
| `GivenIsRetryableTrueOnCompletedStep_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Completed`, both timestamps, `IsRetryable = true` | `CK_WorkflowStepExecutions_IsRetryable_OnlyOnFailed` |

## Key Behaviors

- Raw SQL bypasses `TenantWriteValidationInterceptor` and every domain guard, so these tests exercise only PostgreSQL constraints.
- Each row violates exactly one constraint, which keeps `ConstraintName` deterministic. In particular, the finish-timestamp test uses `Completed` rather than `Running`: a `Running` row with both `CompletedAt` and `CancelledAt` would violate two constraints.
- All 13 constraints from `WorkflowStepExecutionConfiguration` are covered, several with multiple boundary cases.
- PostgreSQL evaluates `btrim("StepId", E' \\t\\n\\r\\f\\v')` and `btrim("ErrorMessage", ...)` with an explicit whitespace set, so tabs and newlines are rejected as well as spaces.
- CLR `null` values must be passed to `ExecuteSqlRawAsync`; `DBNull.Value` is not supported by EF Core's raw-SQL parameter mapping.
- The parent `WorkflowExecution` is raw-inserted with valid defaults; only the step row under test is invalid, so a rejection always comes from `WorkflowStepExecutions`.

## Execution

```bash
dotnet test tests/WorkflowAutomation.Integration.Tests/ --filter FullyQualifiedName~WorkflowStepExecutionConstraintTests
```
