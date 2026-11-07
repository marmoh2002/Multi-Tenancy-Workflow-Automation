# WorkflowPersistenceTests

Integration tests for persisting `Workflow` and `WorkflowVersion` aggregates in PostgreSQL, covering schema constraints, tenant isolation, and cascade behavior.

## Setup

- Uses `PostgreSqlFixture` (Testcontainers `postgres:15-alpine`) with real migrations and production DI via `AddInfrastructurePersistence`.
- Each test is a `[Fact]`; scopes are created explicitly with `_fixture.CreateScope()`.
- `WorkspaceContext.SetContext(workspaceId, userId)` establishes the active tenant per scope.
- Tests seed `ApplicationUser` and `Workspace` first to satisfy foreign keys.

## Test Cases

| Test | Verifies |
| --- | --- |
| `GivenValidWorkflowAndVersion_WhenSaved_CanBeRetrievedInSeparateScope` | Workflow and draft version persist across scopes; `jsonb` definition round-trips and deserializes. |
| `GivenPublishedVersion_WhenActivated_WorkflowAndActiveVersionArePersisted` | Publishing a version, then activating it, persists `IsActive`, `ActiveVersionId`, `IsPublished`, and `PublishedAt`. |
| `GivenDuplicateWorkflowNameInSameWorkspace_WhenSaved_ThrowsDbUpdateException` | Unique constraint on `(WorkspaceId, Name)` rejects duplicate workflow names. |
| `GivenDuplicateVersionNumberForSameWorkflow_WhenSaved_ThrowsDbUpdateException` | Unique constraint on `(WorkspaceId, WorkflowId, VersionNumber)` rejects duplicate versions. |
| `GivenVersionBelongingToDifferentWorkspaceThanWorkflow_WhenSaved_ThrowsDbUpdateException` | Composite FK `(WorkspaceId, WorkflowId)` prevents a version from referencing a workflow in another tenant. |
| `GivenActiveVersionBelongingToDifferentWorkflow_WhenSaved_ThrowsDbUpdateException` | Composite FK `(Id, ActiveVersionId)` prevents a workflow from pointing at another workflow's version. |
| `GivenMultipleWorkspaces_WhenQueryingWorkflowsAndVersions_ReturnsOnlyActiveWorkspaceRecords` | Global query filters return only rows for the active workspace. |
| `GivenNoActiveWorkspace_WhenQueryingWorkflowsAndVersions_ReturnsZeroRecords` | Queries fail closed (empty result, no exception) when no workspace context is set. |
| `GivenWorkflowWithVersions_WhenWorkflowDeleted_VersionsAreCascadeDeleted` | Deleting a workflow cascade-deletes its versions. |
| `GivenWorkspaceWithWorkflows_WhenWorkspaceDeleted_ThrowsDbUpdateException` | Deleting a workspace with workflows is rejected by the FK (`Restrict`/no cascade). |

## Key Behaviors

- Tenant isolation is enforced both on reads (global query filters) and on writes/references (composite foreign keys).
- Schema constraints are validated against actual PostgreSQL migrations, not a generated model.
- Unauthenticated/no-context access returns zero rows rather than throwing.
