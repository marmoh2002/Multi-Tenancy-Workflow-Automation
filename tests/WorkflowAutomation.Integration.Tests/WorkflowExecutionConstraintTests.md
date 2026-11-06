# WorkflowExecutionConstraintTests

Integration tests that bypass EF Core and the domain with raw SQL inserts to verify that PostgreSQL itself rejects invalid `WorkflowExecutions` rows.

## Setup

- Uses `PostgreSqlFixture` (Testcontainers `postgres:15-alpine`) with the real EF Core migrations.
- Each test seeds a valid `ApplicationUser` and `Workspace` so the `WorkspaceId` foreign key is satisfied.
- `InsertInvalidExecutionAsync` executes a parameterized `INSERT` with a complete valid row, overriding only the field(s) under test.
- A valid snapshot and 64-character lowercase hash are used by default so each test isolates one constraint.
- Failures are asserted as `PostgresException`; named check constraints are verified through `ConstraintName`.

## Test Cases

| Test | Invalid input | Expected PostgreSQL rejection |
| --- | --- | --- |
| `GivenUntrimmedWorkflowName_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `WorkflowName = "  Untrimmed Name  "` | `CK_WorkflowExecutions_WorkflowName_TrimmedAndNotEmpty` |
| `GivenEmptyWorkflowName_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `WorkflowName = ""` | `CK_WorkflowExecutions_WorkflowName_TrimmedAndNotEmpty` |
| `GivenNonPositiveVersionNumber_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `VersionNumber = 0` | `CK_WorkflowExecutions_VersionNumber_Positive` |
| `GivenNonPositiveDefinitionSchemaVersion_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `DefinitionSchemaVersion = 0` | `CK_WorkflowExecutions_DefinitionSchemaVersion_Positive` |
| `GivenEmptyDefinitionSnapshot_WhenInsertedRaw_IsRejectedByPostgreSql` | `DefinitionSnapshot = ""` | Check-constraint or invalid-JSON failure (`23514` / `22P02`) |
| `GivenNonObjectDefinitionSnapshot_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `DefinitionSnapshot = "[]"` | `CK_WorkflowExecutions_DefinitionSnapshot_IsObject` |
| `GivenInvalidJsonDefinitionSnapshot_WhenInsertedRaw_IsRejectedByPostgreSql` | `DefinitionSnapshot = "not-json"` | SQLSTATE `22P02` from the `jsonb` cast |
| `GivenMalformedDefinitionHash_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `DefinitionHash = "not-a-sha256-hash"` | `CK_WorkflowExecutions_DefinitionHash_Sha256Hex` |
| `GivenUppercaseDefinitionHash_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `DefinitionHash = "AAAA..."` | `CK_WorkflowExecutions_DefinitionHash_Sha256Hex` |
| `GivenRunningExecutionWithoutStartedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Status = Running`, `StartedAt = null` | `CK_WorkflowExecutions_Running_RequiresStartedAt` |
| `GivenCompletedExecutionWithoutCompletedAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Status = Completed`, `CompletedAt = null` | `CK_WorkflowExecutions_Completed_RequiresCompletedAt` |
| `GivenFailedExecutionWithoutFailureReason_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Status = Failed`, `FailureReason = null` | `CK_WorkflowExecutions_Failed_RequiresCompletedAtAndReason` |
| `GivenCancelledExecutionWithoutCancelledAt_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Status = Cancelled`, `CancelledAt = null` | `CK_WorkflowExecutions_Cancelled_RequiresTimestamps` |
| `GivenCancellationRequestWithoutReason_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `CancellationRequestedAt` set, `CancellationReason = null` | `CK_WorkflowExecutions_CancellationRequest_RequiresReason` |
| `GivenFailedStepIdOnNonFailedExecution_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `FailedStepId` set, `Status != Failed` | `CK_WorkflowExecutions_FailedStepId_OnlyOnFailed` |
| `GivenInvalidExecutionStatus_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Status = 99` | `CK_WorkflowExecutions_Status_Enum_Valid` |
| `GivenInvalidExecutionSource_WhenInsertedRaw_ThrowsCheckConstraintViolation` | `Source = 99` | `CK_WorkflowExecutions_Source_Enum_Valid` |

## Key Behaviors

- Raw SQL bypasses `TenantWriteValidationInterceptor` and domain guards, so these tests exercise only PostgreSQL constraints.
- The `DefinitionSnapshot` column remains `text`; the `jsonb` cast validates JSON syntax and top-level type without rewriting the stored value.
- PostgreSQL does not recompute `DefinitionHash`; the hash constraint only verifies the 64-character lowercase hexadecimal shape.
- Empty snapshots may fail either the `NotEmpty` check or the `jsonb` cast first; both are valid PostgreSQL rejections.
- CLR `null` values must be passed to `ExecuteSqlRawAsync`; `DBNull.Value` is not supported by EF Core's raw-SQL parameter mapping.

## Execution

```bash
dotnet test tests/WorkflowAutomation.Integration.Tests/ --filter FullyQualifiedName~WorkflowExecutionConstraintTests
```
