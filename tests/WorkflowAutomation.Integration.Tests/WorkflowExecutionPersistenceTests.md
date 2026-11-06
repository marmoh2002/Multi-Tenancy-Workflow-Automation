# WorkflowExecutionPersistenceTests

Integration tests for persisting the `WorkflowExecution` aggregate in PostgreSQL, covering immutable snapshot round-tripping, tenant isolation, and execution-history retention after workflow deletion.

## Setup

- Uses `PostgreSqlFixture` (Testcontainers `postgres:15-alpine`) with the real EF Core migrations and production DI via `AddInfrastructurePersistence`.
- Each test uses explicit scopes from `_fixture.CreateScope()`.
- `WorkspaceContext.SetContext(workspaceId, userId)` establishes the active tenant before every query.
- Test data is seeded through the domain (`Workflow.Create`, `WorkflowVersion.CreateDraft`, `Publish`, `WorkflowExecution.Create`) and persisted with `SaveChangesAsync()`.

## Test Cases

| Test | Verifies |
| --- | --- |
| `GivenExecution_WhenSavedAndReloaded_SnapshotFieldsRoundTripAndHashIsIntact` | Reloads a completed execution in a fresh scope and verifies `WorkflowId`, `WorkflowVersionId`, `WorkflowName`, `VersionNumber`, `DefinitionSchemaVersion`, `DefinitionSnapshot`, and `DefinitionHash` round-trip exactly. Recomputes the SHA-256 digest from the stored text, asserts `HasIntactSnapshot()`, and deserializes the snapshot with `GetDefinition()`. |
| `GivenExecutionsInTwoWorkspaces_WhenQuerying_ReturnsOnlyActiveWorkspaceExecutions` | Seeds one execution in each of two workspaces. Each tenant sees only its own row; a query with no workspace context returns zero rows; `IgnoreQueryFilters()` confirms both rows physically exist. |
| `GivenWorkflowWithTerminalExecutions_WhenWorkflowDeleted_VersionsAreRemovedAndHistoryIsRetained` | Creates an active workflow with two published versions and completed, failed, and cancelled executions. Deletes the workflow and verifies both versions cascade away while all three executions remain, with intact snapshots and deserializable definitions. |

## Key Behaviors

- `DefinitionSnapshot` is stored as `text`, so the exact serialized bytes survive the PostgreSQL round-trip and the SHA-256 hash remains valid.
- Global query filters apply to both reads and fail closed when `CurrentWorkspaceId` is `null`.
- `WorkflowExecutions` has no foreign key to `Workflows` or `WorkflowVersions`; deleting a workflow removes its versions but leaves execution history intact.
- Execution deletion is out of scope; the tests only verify the supported retention path.

## Execution

```bash
dotnet test tests/WorkflowAutomation.Integration.Tests/ --filter FullyQualifiedName~WorkflowExecutionPersistenceTests
```
