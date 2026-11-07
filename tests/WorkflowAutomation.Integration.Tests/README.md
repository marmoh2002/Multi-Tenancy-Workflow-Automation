# WorkflowAutomation.Integration.Tests

Integration test suite for PostgreSQL persistence, EF Core global query filters, multi-tenant isolation boundaries, workflow execution snapshots and retention, step execution lifecycle and constraints, and dependency injection container lifetimes.

---

## Test Coverage and Scenarios

### 1. Multi-Tenant Global Query Filters (`GlobalQueryFilterTests.cs`)
Verifies SQL-level row filtering enforced by `ApplicationDbContext`:

* **Fail-Closed Default (No Active Context)**:
  * When `CurrentWorkspaceId` is `null` (such as an unauthenticated request or uninitialized worker), queries for `Workspaces`, `WorkspaceMemberships`, and `WorkspaceConnections` evaluate against SQL `NULL` and return zero records.
* **Tenant Scoping and Cross-Tenant Isolation**:
  * When Workspace A is active, queries return only records belonging to Workspace A. Workspace B records, credentials, and memberships are omitted.
* **Controlled Bootstrap Exception (`IgnoreQueryFilters`)**:
  * Tests the workspace selection flow for authenticated users who have not yet established an active workspace context.
  * Verifies that bypassing global query filters using `.IgnoreQueryFilters()` is safe only when constrained by an explicit filter on the verified `UserId` (`Where(m => m.UserId == userId && m.IsActive)`). Other users' memberships and deactivated memberships remain excluded.

### 2. Tenant Write Validation (`TenantWriteValidationInterceptorTests.cs`)
Verifies write-side isolation enforced by `TenantWriteValidationInterceptor`:

* **Matching Workspace Context**:
  * Entities whose `WorkspaceId` matches the active workspace are persisted.
* **Cross-Workspace Writes**:
  * Inserts, updates, and deletes targeting another workspace are rejected before they reach the database.
* **Missing Workspace Context**:
  * Workspace-scoped writes fail closed when no active workspace is set.
* **Sync and Async Paths**:
  * Both `SaveChanges` and `SaveChangesAsync` enforce the same validation.
* **Scope Isolation**:
  * Sequential scopes can operate on different workspaces without leaking context or interceptor state.

### 3. Dependency Injection Lifetimes (`DependencyInjectionLifetimeTests.cs`)
Verifies service registration and scoping configured by `AddInfrastructurePersistence`:

* **Single-Instance Resolution per Scope (`Assert.Same`)**:
  * Confirms that resolving `WorkspaceContext` (concrete) and `IWorkspaceContext` (interface) within the same scope yields the exact same object reference in memory, ensuring middleware mutations and EF Core queries share state.
* **Cross-Scope Isolation (`Assert.NotSame`)**:
  * Confirms that distinct request scopes receive separate `WorkspaceContext` instances, preventing state bleed between requests.

### 4. Workflow Persistence & Relational Constraints (`WorkflowPersistenceTests.cs`)
Verifies schema constraints, `jsonb` serialization, and lifecycle persistence enforced by PostgreSQL and EF Core:

* **Basic Persistence & JSONB Round-Trip**:
  * Persisting a `Workflow` and draft `WorkflowVersion`, and reading them back in an isolated `DbContext` scope to verify that EF Core properly serializes and deserializes the JSON definition (`jsonb`).
* **Bidirectional Lifecycle Persistence**:
  * Verifies draft creation, publishing, and activating a version on the workflow using two-step persistence to safely resolve circular foreign key dependencies.
* **Unique Constraints**:
  * Uniqueness of `(WorkspaceId, Name)`: duplicate workflow names within the same workspace are rejected with `DbUpdateException`.
  * Uniqueness of `(WorkflowId, VersionNumber)`: duplicate version numbers for a single workflow are rejected with `DbUpdateException`.
* **Composite Foreign Key Boundary Integrity**:
  * Version-to-Workflow: rejects inserting a `WorkflowVersion` targeting another workspace's workflow via composite FK `(WorkspaceId, WorkflowId) -> Workflows(WorkspaceId, Id)`.
  * Active-Version-to-Workflow: rejects setting `ActiveVersionId` to a version belonging to a different workflow via composite FK `(Id, ActiveVersionId) -> WorkflowVersions(WorkflowId, Id)`.
* **Multi-Tenant Query Filters**:
  * Dynamic scoping: queries for `Workflows` and `WorkflowVersions` return only records belonging to the active workspace.
  * Fail-closed default: queries without an active workspace context evaluate against SQL `NULL` and safely return zero records without throwing an exception.
* **Deletion Semantics**:
  * Cascade: deleting a `Workflow` automatically purges all its child `WorkflowVersion` rows in PostgreSQL (`OnDelete(DeleteBehavior.Cascade)`).
  * Restrict: deleting a `Workspace` containing existing workflows is blocked with `DbUpdateException` (`OnDelete(DeleteBehavior.Restrict)`).

### 5. Workflow Version Immutability & Persistence Boundary (`WorkflowVersionImmutabilityInterceptorTests.cs`)
Verifies defense-in-depth rules enforced by `WorkflowVersionImmutabilityInterceptor`:

* **Draft Updates**:
  * Permitted draft modifications (definition, schema version, error notes) are allowed and persisted across scopes.
* **Publication Transition**:
  * Transitioning from draft to published requires `PublishedAt` and no validation errors; valid transitions persist smoothly.
  * A draft may be updated with `UpdateDraft()` and published before a single `SaveChangesAsync()`; the final definition persists with the publication state.
* **Published Version Immutability**:
  * Mutating properties or unpublishing an already published version via ChangeTracker tampering throws `InvalidOperationException`.
* **Permanent Metadata Protection**:
  * Mutating permanent immutable fields (`VersionNumber`, `WorkflowId`, `WorkspaceId`) on drafts is blocked with `InvalidOperationException`.
* **Independent vs. Cascade Deletion**:
  * Discarding unpublished drafts independently succeeds.
  * Deleting a published version independently is strictly prohibited.
  * Deleting a published version alongside its owning `Workflow` in the same unit of work is permitted.

### 6. Workflow Execution Persistence & Retention (`WorkflowExecutionPersistenceTests.cs`)
Verifies the persisted execution snapshot, tenant-scoped reads and writes, and audit retention across workflow deletion.

Detailed test notes: [WorkflowExecutionPersistenceTests.md](WorkflowExecutionPersistenceTests.md).

* **Snapshot Round-Trip**:
  * Persists a completed `WorkflowExecution`, reloads it in a fresh scope, and verifies that `WorkflowName`, `VersionNumber`, `DefinitionSchemaVersion`, `DefinitionSnapshot`, and `DefinitionHash` round-trip exactly.
  * Recomputes the SHA-256 digest from the stored text and confirms `HasIntactSnapshot()` and `GetDefinition()` still succeed.
* **Tenant Isolation**:
  * Seeds one execution in each of two workspaces. Each tenant sees only its own row, and a query without an active workspace context returns zero rows (fail-closed).
  * Uses `IgnoreQueryFilters()` to confirm both rows still exist physically.
* **Cross-Workspace Write Rejection**:
  * Attempts to insert an execution owned by another workspace under the active tenant context; `TenantWriteValidationInterceptor` throws before persistence, and the row is confirmed absent via `IgnoreQueryFilters()`.
* **Execution-History Retention**:
  * Deletes a workflow with two published versions and completed, failed, and cancelled executions.
  * Confirms both versions cascade away while all three executions remain with intact, deserializable snapshots.
  * Verifies the associated step attempts survive with their parent executions.

### 7. Workflow Execution Check Constraints (`WorkflowExecutionConstraintTests.cs`)
Verifies PostgreSQL check constraints by inserting raw execution rows that bypass EF Core and the domain.

Detailed test notes: [WorkflowExecutionConstraintTests.md](WorkflowExecutionConstraintTests.md).

* **Snapshot Integrity**:
  * Rejects empty, non-object, and invalid-JSON `DefinitionSnapshot` values.
* **Name, Version, and Hash**:
  * Rejects untrimmed or empty `WorkflowName`, non-positive `VersionNumber` or `DefinitionSchemaVersion`, and malformed or uppercase `DefinitionHash` values.
* **Status, Timestamps, and Cancellation**:
  * Rejects `Running` without `StartedAt`, `Completed` without `CompletedAt`, `Failed` without a reason, `Cancelled` without `CancelledAt`, cancellation requests without a reason, and `FailedStepId` on non-failed rows.
* **Enum Guards**:
  * Rejects undefined `Status` and `Source` values.

### 8. Workflow Step Execution Persistence (`WorkflowStepExecutionPersistenceTests.cs`)
Verifies step-attempt lifecycle persistence, per-attempt uniqueness, tenant isolation, and foreign-key behavior.

Detailed test notes: [WorkflowStepExecutionPersistenceTests.md](WorkflowStepExecutionPersistenceTests.md).

* **Lifecycle Round-Trips**:
  * Persists and reloads `Pending`, `Completed`, `Failed`, `Cancelled`, and `Skipped` attempts, verifying timestamps, `ErrorMessage` trimming, `OutputMetadata`, `IsRetryable`, and the computed `FinishedAt`.
* **Attempt Uniqueness**:
  * Rejects a duplicate `(ExecutionId, StepId, AttemptNumber)` with SQLSTATE `23505` on `IX_WorkflowStepExecutions_ExecutionId_StepId_AttemptNumber`, and confirms only the original row remains.
* **Tenant Isolation**:
  * Global query filters return only the active workspace's attempts, fail closed without context, and `IgnoreQueryFilters()` confirms both tenants' rows physically exist.
* **Write Boundary**:
  * `TenantWriteValidationInterceptor` rejects a step attempt targeting another workspace's execution before it reaches PostgreSQL.
* **Composite Foreign Key**:
  * A raw insert claiming a different `WorkspaceId` than its parent execution is rejected with SQLSTATE `23503` by `FK_WorkflowStepExecutions_Executions_WorkspaceId_ExecutionId`.
* **Cascade Deletion**:
  * Deleting a parent execution cascades all of its attempts, including retries, while the workflow and version remain.

### 9. Workflow Step Execution Check Constraints (`WorkflowStepExecutionConstraintTests.cs`)
Verifies all 13 PostgreSQL check constraints on `WorkflowStepExecutions` through raw inserts that bypass EF Core and the domain.

Detailed test notes: [WorkflowStepExecutionConstraintTests.md](WorkflowStepExecutionConstraintTests.md).

* **StepId and AttemptNumber Integrity**:
  * Rejects untrimmed or empty `StepId` values and non-positive `AttemptNumber` values.
* **Enum Guard**:
  * Rejects undefined `Status` values.
* **Status and Timestamp Consistency**:
  * Rejects timestamps on `Pending`, a missing `StartedAt` on `Running`, missing timestamps on `Completed`, missing timestamps or a blank reason on `Failed`, a missing `CancelledAt` on `Cancelled`, and invalid timestamps on `Skipped`.
  * Rejects rows carrying both `CompletedAt` and `CancelledAt`.
* **Conditional Fields**:
  * Rejects `ErrorMessage` outside `Failed`, `OutputMetadata` outside `Completed`, and `IsRetryable` outside `Failed`.

---

## Database Strategy

The suite uses PostgreSQL via Testcontainers for every database-bound test:

* **PostgreSQL via Testcontainers** (`GlobalQueryFilterTests`, `TenantWriteValidationInterceptorTests`, `WorkflowPersistenceTests`, `WorkflowConstraintTests`, `WorkflowConcurrencyTests`, `WorkflowVersionImmutabilityInterceptorTests`, `WorkflowExecutionPersistenceTests`, `WorkflowExecutionConstraintTests`, `WorkflowStepExecutionPersistenceTests`, `WorkflowStepExecutionConstraintTests`):
  * Uses `PostgreSqlFixture` to start a disposable `postgres:15-alpine` container, build the production DI container, and apply the committed EF migrations through `MigrateAsync`.
  * Exercises the Npgsql provider and the actual migration baseline rather than a model-generated schema.

---

## Execution

Run integration tests:

```bash
dotnet test tests/WorkflowAutomation.Integration.Tests/WorkflowAutomation.Integration.Tests.csproj
```

Run all solution tests:

```bash
dotnet test WorkflowAutomation.slnx
```
