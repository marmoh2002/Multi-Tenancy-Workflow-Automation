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

### 2. Dependency Injection Lifetimes (`DependencyInjectionLifetimeTests.cs`)
Verifies service registration and scoping configured by `AddInfrastructurePersistence`:

* **Single-Instance Resolution per Scope (`Assert.Same`)**:
  * Confirms that resolving `WorkspaceContext` (concrete) and `IWorkspaceContext` (interface) within the same scope yields the exact same object reference in memory, ensuring middleware mutations and EF Core queries share state.
* **Cross-Scope Isolation (`Assert.NotSame`)**:
  * Confirms that distinct request scopes receive separate `WorkspaceContext` instances, preventing state bleed between requests.

---

## Database Strategy

* **Provider**: SQLite In-Memory (`Microsoft.EntityFrameworkCore.Sqlite`).
* **Rationale**:
  * SQLite is a relational database engine that tests EF Core's actual SQL query translation and schema generation (`EnsureCreated()`).
  * Executes in-process without network overhead or dependencies on external container runtimes.
* **Connection Lifecycle**:
  * In-memory SQLite databases persist only while the underlying connection remains open. The test fixture maintains an open `SqliteConnection("DataSource=:memory:")` in the constructor and disposes it on teardown.

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
