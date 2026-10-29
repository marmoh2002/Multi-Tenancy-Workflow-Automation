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

---

## Database Strategy

The suite uses two providers, each chosen for the behavior under test:

* **SQLite In-Memory (`GlobalQueryFilterTests`)**:
  * Tests EF Core global query filter translation and fail-closed behavior without external dependencies.
  * The test class keeps a `SqliteConnection("DataSource=:memory:")` open for its lifetime and disposes it on teardown, since the in-memory database exists only while the connection remains open.
* **PostgreSQL via Testcontainers (`TenantWriteValidationInterceptorTests`)**:
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
