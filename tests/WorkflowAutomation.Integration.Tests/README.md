# WorkflowAutomation.Integration.Tests

Integration test suite for database persistence, EF Core global query filters, multi-tenant isolation boundaries, and dependency injection container lifetimes.

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
* **Published Version Immutability**:
  * Mutating properties or unpublishing an already published version via ChangeTracker tampering throws `InvalidOperationException`.
* **Permanent Metadata Protection**:
  * Mutating permanent immutable fields (`VersionNumber`, `WorkflowId`, `WorkspaceId`) on drafts is blocked with `InvalidOperationException`.
* **Independent vs. Cascade Deletion**:
  * Discarding unpublished drafts independently succeeds.
  * Deleting a published version independently is strictly prohibited.
  * Deleting a published version alongside its owning `Workflow` in the same unit of work is permitted.

---

## Database Strategy

The suite uses two providers, each chosen for the behavior under test:

* **SQLite In-Memory (`GlobalQueryFilterTests`)**:
  * Tests EF Core global query filter translation and fail-closed behavior without external dependencies.
  * The test class keeps a `SqliteConnection("DataSource=:memory:")` open for its lifetime and disposes it on teardown, since the in-memory database exists only while the connection remains open.
* **PostgreSQL via Testcontainers (`TenantWriteValidationInterceptorTests`, `WorkflowPersistenceTests`)**:
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
