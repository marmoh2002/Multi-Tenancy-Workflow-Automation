# WorkflowVersionImmutabilityInterceptor

## 1. Purpose & Scope

`WorkflowVersionImmutabilityInterceptor` is an EF Core `SaveChangesInterceptor` that acts as a **persistence guard** for `WorkflowVersion` lifecycle rules.

While domain methods (`CreateDraft`, `UpdateDraft`, `Publish`) enforce rules in application memory, this interceptor runs right before `SaveChanges` / `SaveChangesAsync` to ensure no direct property tampering, untracked mutations, or illegal deletions reach the database.

---

## 2. Enforced Lifecycle Rules

### A. Added (`EntityState.Added`)
- **Drafts (`IsPublished == false`)**: Must not have a `PublishedAt` timestamp. (Validation errors may be present or null).
- **Pre-published (`IsPublished == true`)**: Must have a non-null `PublishedAt` timestamp and `ValidationErrorsJson == null`.

### B. Modified (`EntityState.Modified`)
1. **Permanently Immutable Properties**:
   `Id`, `WorkspaceId`, `WorkflowId`, `VersionNumber`, `CreatedAt`.
   Any modification attempt throws `InvalidOperationException`.
2. **Originally Published Versions**:
   Determined via `entry.Property(v => v.IsPublished).OriginalValue == true`.
   - Cannot change any application property.
   - Cannot unpublish (`IsPublished` cannot be flipped back to `false`).
3. **Draft Transitions (`OriginalValue` was `false`)**:
   - **Transitioning to Published**:
     - `PublishedAt` must be non-null.
     - `ValidationErrorsJson` must be `null`.
     - Only publication fields and draft-editable properties may be modified in that save.
   - **Remaining a Draft**:
     - `PublishedAt` must remain `null`.
     - Only allowlisted properties can change: `SerializedDefinition`, `DefinitionSchemaVersion`, `ValidationErrorsJson`, `UpdatedAt`.

### C. Deleted (`EntityState.Deleted`)
- **Drafts (`OriginalValue` of `IsPublished == false`)**: May be deleted independently.
- **Published Versions**: Cannot be deleted independently.
  - **Exception**: Allowed only if the owning `Workflow` is tracked as `Deleted` in the same `ChangeTracker` unit of work.
  - Ownership matching uses the version's original `WorkflowId` and `WorkspaceId` against the deleted workflow's original `Id` and `WorkspaceId`.

---

## 3. EF Core Mechanics & Syntax Quirks

### 1. `DetectChanges()` Call
```csharp
context.ChangeTracker.DetectChanges();
```
- EF Core doesn't always snapshot property changes immediately for complex operations. Calling `DetectChanges()` explicitly ensures `prop.IsModified`, `CurrentValue`, and `OriginalValue` are fully synchronized before validation runs.

### 2. `OriginalValue` vs `CurrentValue`
```csharp
var wasPublished = (bool)entry.Property(nameof(WorkflowVersion.IsPublished)).OriginalValue!;
```
- **Why this matters**: A malicious or buggy caller could take a published version and mutate `version.IsPublished = false` in memory before editing steps.
- Checking `entry.Entity.IsPublished` alone would be tricked into treating it as a draft.
- Checking `OriginalValue` reads the state when EF Core loaded the row from PostgreSQL, closing the bypass.

### 3. Concurrency Token Exemption (`IsConcurrencyToken`)
```csharp
if (prop.Metadata.IsConcurrencyToken)
{
    continue; // e.g. PostgreSQL xmin
}
```
- Concurrency tokens (such as `xmin`) are mutated by the database provider on every update.
- Without this check, a published version update (or draft edit) would fail because the concurrency token column is flagged as modified.

### 4. Sibling Entity Inspection
```csharp
var isOwningWorkflowDeleted = context.ChangeTracker
    .Entries<Workflow>()
    .Any(w => w.State == EntityState.Deleted
           && Equals(w.Property(nameof(Workflow.Id)).OriginalValue, originalWorkflowId)
           && Equals(w.Property(nameof(Workflow.WorkspaceId)).OriginalValue, originalWorkspaceId));
```
- Interceptors can inspect other entities in the same `ChangeTracker`.
- For loaded parent-child relationships, when a parent `Workflow` is removed, EF Core client-cascades deletion to loaded versions. This query confirms that the version deletion is part of that cascade, not an isolated deletion.
- Unloaded versions never appear in `ChangeTracker.Entries<WorkflowVersion>()`; their cascade is handled directly by PostgreSQL foreign key `ON DELETE CASCADE`.
