## What It Does

`TenantWriteValidationInterceptor` is an EF Core **save interceptor** that acts as a **write-side guard** for your multi-tenancy model. It hooks into `SavingChangesAsync` — right before EF Core flushes changes to the database — and enforces two rules:

1. **No workspace context → no tenant writes.** If `IWorkspaceContext.HasCurrentWorkspace` is `false`, it scans the ChangeTracker for any Added/Modified/Deleted entities implementing `IMustHaveWorkspace`. If any are found, it throws — preventing accidental unscoped writes.

2. **Active workspace → ownership check.** If a workspace *is* active, it iterates every tracked `IMustHaveWorkspace` entity being mutated and verifies its `WorkspaceId` matches `CurrentWorkspaceId`. A mismatch throws — preventing **cross-tenant data corruption**.

## Why It Matters

We already have a **read-side** guard (global query filters that scope `SELECT` queries to the current workspace). This interceptor is the **write-side complement**. Without it, a bug in the application layer could silently insert, update, or delete an entity belonging to a different workspace. The interceptor makes that impossible at the persistence boundary.

## Key Architectural Considerations & Learning Notes

### 1. Guarding Both Sync and Async Execution Paths
- **The Concept:** EF Core provides two methods to commit changes: `SaveChanges()` (synchronous, blocking) and `SaveChangesAsync()` (asynchronous, non-blocking).
- **The Risk:** Overriding only `SavingChangesAsync` leaves `SaveChanges()` completely unguarded. Any synchronous call (e.g., in a test, script, or accidentally written code) would bypass the interceptor entirely.
- **Resolution:** Always override both `SavingChanges` and `SavingChangesAsync` and route them through the same validation logic (`ValidateWorkspaceOwnership`). *(Implemented & verified)*.

### 2. ChangeTracker Mechanics & Performance
- **The Concept:** EF Core's `ChangeTracker` keeps an in-memory record (like a clipboard) of every entity tracked in the current `DbContext` session and its state (`Added`, `Modified`, `Deleted`, `Unchanged`).
- **How the Interceptor Uses It:** Calling `context.ChangeTracker.Entries<IMustHaveWorkspace>()` iterates through all tracked entities in memory to filter for those implementing the workspace interface.
- **Performance Consideration:**
  - **Standard Web Requests (OLTP):** For normal operations (1–10 entities), this scan takes microseconds and has virtually zero perceptible overhead.
  - **Bulk / Batch Operations:** If an operation loads or inserts thousands of rows (e.g., a 10,000-row file import), iterating the ChangeTracker multiple times adds CPU and memory pressure.
  - **Rule of Thumb:** EF Core `DbContext` and interceptors are optimized for transactional units of work. For massive bulk operations, specialized tools (e.g., `EFCore.BulkExtensions` or PostgreSQL `COPY`) that bypass the ChangeTracker should be used instead.


