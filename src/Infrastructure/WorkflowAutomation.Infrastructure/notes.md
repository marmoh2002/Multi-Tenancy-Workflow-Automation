# Multi-Tenancy Persistence & Isolation Notes

This document summarizes the core design, implementation, and lessons learned for workspace multi-tenancy in EF Core.

---

## 1. Defense-in-Depth Isolation Model

Multi-tenancy isolation cannot rely solely on query filters. We use a two-tier defense model:

| Layer | Component | Mechanism | Scope |
|:---|:---|:---|:---|
| **Read-Side** | Global Query Filters | Appends `WHERE WorkspaceId = @CurrentWorkspaceId` | `SELECT` queries |
| **Write-Side** | `TenantWriteValidationInterceptor` | Validates `ChangeTracker` states against active context | `INSERT`, `UPDATE`, `DELETE` (`SaveChanges` / `SaveChangesAsync`) |

---

## 2. Read-Side: Global Query Filters (`ApplicationDbContext.MultiTenancy.cs`)

- **Convention-based automation**: Scans the EF model via reflection for all entities implementing `IMustHaveWorkspace` and applies the filter dynamically.
- **Static closure trap avoided**: Filters reference the instance field `_workspaceContext.CurrentWorkspaceId` inside an instance method on `ApplicationDbContext`. This ensures EF Core evaluates the filter dynamically per query (`@ef_filter__CurrentWorkspaceId`) rather than baking a static `null` closure into the cached model (`WHERE 0`).
- **Null-lifting**: Comparing `w.Id == _workspaceContext.CurrentWorkspaceId` (without `?? Guid.Empty` unwrapping) lets C# safely lift the comparison to `Nullable<Guid>`, allowing EF Core to send SQL `NULL` when no workspace is active and safely return 0 records (fail-closed).
- **Controlled bootstrap**: Authenticated users without an active workspace query memberships using `.IgnoreQueryFilters().Where(m => m.UserId == userId && m.IsActive)`.

---

## 3. Write-Side: Tenant Write Validation Interceptor (`TenantWriteValidationInterceptor.cs`)

- **Why it is needed**: Global query filters do **not** inspect entities passed to `Add()`, `Update()`, or `Remove()`. Without write-side validation, cross-tenant references or unauthenticated writes could be saved to the database.
- **Validation rules**:
  1. **No Active Workspace**: If `!_workspaceContext.HasCurrentWorkspace`, any mutated `IMustHaveWorkspace` entity throws an `InvalidOperationException`.
  2. **Cross-Workspace Mutation**: If `_workspaceContext.HasCurrentWorkspace`, every mutated `IMustHaveWorkspace` entity must have `WorkspaceId == _workspaceContext.CurrentWorkspaceId`. Any mismatch throws an `InvalidOperationException`.
- **Complete lifecycle coverage**: Overrides both `SavingChanges` (synchronous) and `SavingChangesAsync` (asynchronous), inspecting all mutation states: `Added`, `Modified`, and `Deleted`.

---

## 4. Scoped Dependency Injection Registration (`DependencyInjection.cs`)

```csharp
// 1. Concrete WorkspaceContext registered as Scoped
services.AddScoped<WorkspaceContext>();

// 2. IWorkspaceContext resolves to the exact same instance in the scope
services.AddScoped<IWorkspaceContext>(sp => sp.GetRequiredService<WorkspaceContext>());

// 3. Interceptor registered as Scoped (matches IWorkspaceContext lifetime)
services.AddScoped<TenantWriteValidationInterceptor>();

// 4. DbContext resolves interceptor from current scope; unpooled to prevent leakage
services.AddDbContext<ApplicationDbContext>(
    (serviceProvider, options) =>
    {
        options.UseNpgsql(connectionString);
        options.AddInterceptors(serviceProvider.GetRequiredService<TenantWriteValidationInterceptor>());
    },
    contextLifetime: ServiceLifetime.Scoped,
    optionsLifetime: ServiceLifetime.Scoped);
```

- **Captive dependency prevention**: Because `IWorkspaceContext` varies per HTTP request or background message, both the interceptor and `ApplicationDbContext` must be `Scoped`.
- **No context pooling**: `AddDbContextPool` is avoided to guarantee zero cross-tenant state leakage between operations.

---

## 5. Integration Testing (`TenantWriteValidationInterceptorTests.cs`)

- **Real PostgreSQL via Testcontainers**: Uses `Testcontainers.PostgreSql 4.16.0` to run tests against real PostgreSQL rather than mocks.
- **Built-in readiness strategy**: Relies on Testcontainers' internal PostgreSQL ping (`pg_isready`) rather than port-only wait checks.
- **Class fixture lifecycle**: The container is started once per test class (`IClassFixture<PostgreSqlFixture>`) and disposed at the end.
- **Isolation via unique values**: Tests share the container safely by generating unique `Guid` values and unique names for constrained fields (`UserName`, `Email`, `ConnectionName`).
- **Verification via `.IgnoreQueryFilters()`**: For rejected writes, verification queries use `.IgnoreQueryFilters()` in a fresh scope to prove the invalid entity was genuinely rejected and never reached the database.
