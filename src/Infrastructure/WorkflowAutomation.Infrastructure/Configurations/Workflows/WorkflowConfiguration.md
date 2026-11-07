# Workflow Entity Configurations

This document explains the EF Core configurations for the `Workflow` and `WorkflowVersion` entities in `WorkflowAutomation.Infrastructure.Configurations.Workflows`. It covers what the mappings do, why they were designed this way, and concrete examples of the database rules they enforce.

It describes the **current implementation**. Items that are still planned are marked explicitly.

> [!NOTE]
> **Current status:** `WorkflowConfiguration` and `WorkflowVersionConfiguration` exist and are automatically discovered by `ApplicationDbContext`. No migration has been generated for the `Workflows` and `WorkflowVersions` tables yet, so they are not part of the migrated schema. Check constraints, concurrency tokens, and the version immutability interceptor described in `phase3_plan.md` are not implemented yet.

---

## Table of Contents

- [1. What Has Been Implemented](#1-what-has-been-implemented)
- [2. How the Configurations Are Loaded](#2-how-the-configurations-are-loaded)
- [3. The Relationship Model](#3-the-relationship-model)
- [4. WorkflowConfiguration](#4-workflowconfiguration)
- [5. WorkflowVersionConfiguration](#5-workflowversionconfiguration)
- [6. Composite Foreign Keys Explained](#6-composite-foreign-keys-explained)
- [7. How the Two Configurations Work Together](#7-how-the-two-configurations-work-together)
- [8. Multi-Tenancy and Write Protection](#8-multi-tenancy-and-write-protection)
- [9. Design Decisions and Trade-offs](#9-design-decisions-and-trade-offs)
- [10. What Is Not Implemented Yet](#10-what-is-not-implemented-yet)
- [11. Examples and Verification](#11-examples-and-verification)
- [12. Related Documentation](#12-related-documentation)

---

## 1. What Has Been Implemented

| Area | Implemented Behavior |
| :--- | :--- |
| Table mapping | `Workflows` and `WorkflowVersions` |
| Primary keys | `Workflow.Id`, `WorkflowVersion.Id` |
| Alternate keys | `Workflow (WorkspaceId, Id)`, `WorkflowVersion (WorkflowId, Id)` |
| Unique indexes | `Workflows (WorkspaceId, Name)`, `WorkflowVersions (WorkflowId, VersionNumber)` |
| Workspace ownership | Both entities have a required `WorkspaceId` with a restrictive foreign key to `Workspaces` |
| Creator ownership | `Workflow.CreatedByUserId` has a restrictive foreign key to `AspNetUsers` |
| Version ownership | `WorkflowVersion (WorkspaceId, WorkflowId)` references `Workflow (WorkspaceId, Id)` |
| Active version ownership | `Workflow (Id, ActiveVersionId)` references `WorkflowVersion (WorkflowId, Id)` |
| JSON storage | `SerializedDefinition` and `ValidationErrorsJson` use PostgreSQL `jsonb` |
| Audit timestamps | `CreatedAt` is required; `UpdatedAt` is optional |
| Not implemented yet | Check constraints, concurrency (`xmin`), immutability interceptor, migration |

---

## 2. How the Configurations Are Loaded

`ApplicationDbContext.OnModelCreating` calls:

```csharp
builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
```

This means every class implementing `IEntityTypeConfiguration<T>` in the Infrastructure assembly is applied automatically. There is no need to register `WorkflowConfiguration` or `WorkflowVersionConfiguration` manually.

Because the configurations call `modelBuilder.Entity<Workflow>()` and `modelBuilder.Entity<WorkflowVersion>()`, both entity types are part of the EF model. The context also exposes them for querying:

```csharp
public DbSet<Workflow> Workflows => Set<Workflow>();
public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();
```

> [!NOTE]
> The `DbSet` properties make querying convenient, but they are not what makes the mappings active. The `IEntityTypeConfiguration` classes are what register the entity types and their database rules with the model.

---

## 3. The Relationship Model

At a high level:

```text
Workspace
   │ 1
   │
   │ *
Workflow
   │ 1
   │
   │ *
WorkflowVersion

Workflow.ActiveVersionId ──────> one of its own WorkflowVersion rows
```

| Relationship | Cardinality | Meaning |
| :--- | :--- | :--- |
| `Workspace` → `Workflow` | 1 to many | A workspace owns its workflows. Deleting a workspace with workflows is restricted. |
| `Workflow` → `WorkflowVersion` | 1 to many | A workflow owns all of its versions. Versions cannot belong to a workflow in another workspace. |
| `Workflow.ActiveVersionId` | 0 or 1 | A workflow may select one of its own versions as active. It may have no active version yet. |

A version is a **snapshot** of a workflow definition. Editing a workflow creates a new version rather than overwriting an old one.

---

## 4. WorkflowConfiguration

File: `WorkflowConfiguration.cs`

### 4.1 Property Mapping

| Property | Mapping | Why |
| :--- | :--- | :--- |
| `Id` | Primary key | Workflow identity. |
| `WorkspaceId` | Required; FK to `Workspaces.Id`, `Restrict` | Every workflow belongs to exactly one workspace. Restrict prevents deleting a workspace that still owns workflows. |
| `CreatedByUserId` | Required; FK to `AspNetUsers.Id`, `Restrict` | Audit ownership. Deleting a user must not cascade-delete workflow history. |
| `Name` | Required, maximum 200 characters | User-facing workflow name. The domain also trims and validates it. |
| `Description` | Optional, maximum 2,000 characters | Optional details. The domain also trims and validates it. |
| `IsActive` | Required, database default `false` | New workflows must be explicitly activated. The default protects direct inserts that bypass the domain. |
| `ActiveVersionId` | Optional | A workflow starts without a selected version. The domain requires a version before `Activate()` succeeds. |
| `CreatedAt` | Required | Set once by the domain through `IClock`. |
| `UpdatedAt` | Optional | Set only when the domain performs a real state change. |

### 4.2 Unique Name Within a Workspace

```csharp
builder.HasIndex(w => new { w.WorkspaceId, w.Name })
    .IsUnique();
```

This prevents two workflows in the same workspace from having the same name. The same name is allowed in different workspaces because the index starts with `WorkspaceId`.

Example:

| Workspace | Workflow Name | Result |
| :--- | :--- | :--- |
| `WS-A` | `Sync CRM` | Allowed |
| `WS-A` | `Sync CRM` | Rejected by the unique index |
| `WS-B` | `Sync CRM` | Allowed |

> [!NOTE]
> PostgreSQL string comparison is case-sensitive by default, so `Sync CRM` and `sync crm` are considered different names unless a case-insensitive index or `citext` column is introduced later.

### 4.3 Workspace and Creator Foreign Keys

```csharp
builder.HasOne<Workspace>()
    .WithMany()
    .HasForeignKey(w => w.WorkspaceId)
    .OnDelete(DeleteBehavior.Restrict);

builder.HasOne<ApplicationUser>()
    .WithMany()
    .HasForeignKey(w => w.CreatedByUserId)
    .OnDelete(DeleteBehavior.Restrict);
```

Both relationships use `Restrict`:

- A workspace cannot be deleted while it still has workflows.
- A user cannot be deleted while they are recorded as the creator of a workflow.

This protects tenant data and audit history from accidental cascading deletion. The application must explicitly archive or reassign data before deletion.

There are no navigation properties on the entities. The relationships are configured with raw `Guid` foreign keys, which keeps modules decoupled while still enforcing referential integrity in the shared database.

### 4.4 Active Version Relationship

```csharp
builder.HasOne<WorkflowVersion>()
    .WithMany()
    .HasForeignKey(w => new { w.Id, w.ActiveVersionId })
    .HasPrincipalKey(v => new { v.WorkflowId, v.Id })
    .OnDelete(DeleteBehavior.Restrict);
```

This is the most important mapping in `WorkflowConfiguration`. It does **not** simply check that `ActiveVersionId` exists. It checks that the selected version belongs to **this same workflow**.

The foreign key is:

```text
Workflow (Id, ActiveVersionId)
        references
WorkflowVersion (WorkflowId, Id)
```

When `ActiveVersionId` is `NULL`, the optional foreign key is not enforced. When it is set, the pair `(Workflow.Id, ActiveVersionId)` must match an existing `(WorkflowVersion.WorkflowId, WorkflowVersion.Id)` pair.

Example:

| Workflow | `ActiveVersionId` | Version actually belongs to | Result |
| :--- | :--- | :--- | :--- |
| `W1` | `V1` | `W1` | Allowed |
| `W1` | `V2` | `W1` | Allowed by the FK; publication is checked by the domain |
| `W1` | `V3` | `W2` | Rejected: `(W1, V3)` does not match `(W2, V3)` |

The database enforces **ownership**, not **publication status**. A draft version that belongs to the workflow is still a valid target for the foreign key. `Workflow.ActivateVersion` is what rejects unpublished versions:

```csharp
if (version.WorkflowId != Id)
    throw new InvalidOperationException("The version belongs to a different workflow.");

if (version.WorkspaceId != WorkspaceId)
    throw new InvalidOperationException("The version belongs to a different workspace.");

if (!version.IsPublished)
    throw new InvalidOperationException("Cannot activate an unpublished version.");
```

This is defense in depth: the domain provides clear error messages, while the database prevents cross-workflow references even if application code is bypassed.

### 4.5 Alternate Key `(WorkspaceId, Id)`

```csharp
builder.HasAlternateKey(w => new { w.WorkspaceId, w.Id });
```

An alternate key is **not** a second primary key. It is a unique key that other tables can reference as a foreign key target.

The primary key is only `Id`. A composite foreign key from `WorkflowVersion` needs a matching unique principal key on `Workflow`. The alternate key `(WorkspaceId, Id)` provides that target:

```text
WorkflowVersion (WorkspaceId, WorkflowId)
        references
Workflow (WorkspaceId, Id)
```

Without this alternate key, EF Core could not create the composite ownership foreign key.

---

## 5. WorkflowVersionConfiguration

File: `WorkflowVersionConfiguration.cs`

### 5.1 Property Mapping

| Property | Mapping | Why |
| :--- | :--- | :--- |
| `Id` | Primary key | Version identity. |
| `WorkspaceId` | Required; FK to `Workspaces.Id`, `Restrict` | Direct tenant ownership. Also part of the composite workflow foreign key. |
| `WorkflowId` | Required | The workflow this version belongs to. |
| `VersionNumber` | Required | Business version number, unique within a workflow. |
| `DefinitionSchemaVersion` | Required | Describes the JSON definition shape. It is separate from `VersionNumber`. |
| `IsPublished` | Required | Publication state. Only published versions may be selected as active. |
| `SerializedDefinition` | Required, `jsonb` | The complete workflow definition snapshot. |
| `ValidationErrorsJson` | Optional, `jsonb` | DAG validation errors for invalid drafts. `NULL` means valid. |
| `PublishedAt` | Optional | Timestamp of publication. `NULL` while the version is a draft. |
| `CreatedAt` | Required | Set once when the draft is created. |
| `UpdatedAt` | Optional | Set when the draft is published. |

### 5.2 Unique Version Number Within a Workflow

```csharp
builder.HasIndex(v => new { v.WorkflowId, v.VersionNumber })
    .IsUnique();
```

Version numbers are scoped to a workflow, not globally. Every workflow can have its own version `1`, `2`, `3`, and so on.

Example:

| Workflow | Version Number | Result |
| :--- | :--- | :--- |
| `W1` | `1` | Allowed |
| `W1` | `1` | Rejected by the unique index |
| `W2` | `1` | Allowed, because it is a different workflow |

The unique index prevents duplicates but does **not allocate** the next version number. Callers must supply a positive version number, and the domain rejects values `<= 0`. Concurrent allocation is deferred to a later phase.

### 5.3 JSON Definition Storage

```csharp
builder.Property(v => v.SerializedDefinition)
    .HasColumnType("jsonb")
    .IsRequired();

builder.Property(v => v.ValidationErrorsJson)
    .HasColumnType("jsonb")
    .IsRequired(false);
```

`jsonb` stores the definition as binary JSON that PostgreSQL can query and validate. The version row stores the complete definition, so there are no separate step or dependency tables in this phase.

A valid definition is stored like this:

```json
{"steps":[{"id":"fetch-data","type":"HttpRequest","dependsOn":[],"parameters":{"url":"https://api.example.com/items","method":"GET"}},{"id":"transform-data","type":"DataTransform","dependsOn":["fetch-data"],"parameters":{"format":"json"}}]}
```

A draft with DAG errors stores an array of error strings:

```json
["Cycle detected involving steps: step-a, step-b."]
```

For a valid version, `ValidationErrorsJson` must be `null` at the entity level so the database column is SQL `NULL`. It must never be the JSON text `"null"`.

Why store a snapshot instead of normalized step tables?

- A version is immutable by design. A complete JSON snapshot preserves exactly what was published.
- Reading a version is a single row lookup instead of joining many step and dependency tables.
- The JSON shape can evolve with `DefinitionSchemaVersion`.
- The trade-off is that individual steps are not relational rows. Querying or updating a single step inside the JSON requires PostgreSQL JSON operators instead of normal joins.

### 5.4 Workspace and Workflow Foreign Keys

```csharp
builder.HasOne<Workspace>()
    .WithMany()
    .HasForeignKey(v => v.WorkspaceId)
    .OnDelete(DeleteBehavior.Restrict);

builder.HasOne<Workflow>()
    .WithMany()
    .HasForeignKey(v => new { v.WorkspaceId, v.WorkflowId })
    .HasPrincipalKey(w => new { w.WorkspaceId, w.Id })
    .OnDelete(DeleteBehavior.Cascade);
```

The workspace foreign key uses `Restrict`, matching `WorkflowConfiguration`: a workspace cannot be deleted while it owns versions.

The workflow foreign key uses **both** `WorkspaceId` and `WorkflowId`:

```text
WorkflowVersion (WorkspaceId, WorkflowId)
        references
Workflow (WorkspaceId, Id)
```

This prevents a version from claiming to belong to a workflow in another workspace. A simple `WorkflowId` foreign key would allow this cross-tenant mistake; the composite key closes that gap.

Example:

| Version Workspace | Version Workflow | Workflow actually belongs to | Result |
| :--- | :--- | :--- | :--- |
| `WS-A` | `W1` | `WS-A` | Allowed |
| `WS-B` | `W1` | `WS-A` | Rejected: `(WS-B, W1)` does not match `(WS-A, W1)` |

The current configuration uses `Cascade` for the workflow relationship. The workflow is the owner of its versions, so deleting a workflow removes its version history instead of leaving orphan rows.

> [!NOTE]
> `phase3_plan.md` sketched `Restrict` for this relationship. The current code uses `Cascade`. Both are defensible:
> - `Cascade` treats versions as owned child rows and cleans them up automatically.
> - `Restrict` preserves version history and forces an explicit deletion workflow.
>
> If version history must survive workflow deletion, change this to `Restrict` and delete versions explicitly. If `Cascade` is kept, verify the delete behavior in an integration test because `Workflow.ActiveVersionId` also uses `Restrict`.

### 5.5 Alternate Key `(WorkflowId, Id)`

```csharp
builder.HasAlternateKey(v => new { v.WorkflowId, v.Id });
```

This unique key is the principal key for the active-version foreign key on `Workflow`:

```text
Workflow (Id, ActiveVersionId)
        references
WorkflowVersion (WorkflowId, Id)
```

It is separate from the unique index on `(WorkflowId, VersionNumber)`:

- The alternate key identifies a version row for foreign key purposes.
- The unique index enforces the business rule that version numbers do not repeat inside a workflow.

Both are unique, but they serve different purposes and use different columns.

---

## 6. Composite Foreign Keys Explained

Composite foreign keys are the part of these configurations that is easiest to misunderstand. This section walks through both of them.

### 6.1 Version Belongs to a Workflow in the Same Workspace

Configuration:

```csharp
.HasForeignKey(v => new { v.WorkspaceId, v.WorkflowId })
.HasPrincipalKey(w => new { w.WorkspaceId, w.Id })
```

The foreign key columns and the principal key columns are matched **by position**:

| Position | WorkflowVersion column | Workflow column |
| :---: | :--- | :--- |
| 1 | `WorkspaceId` | `WorkspaceId` |
| 2 | `WorkflowId` | `Id` |

The second position compares `WorkflowVersion.WorkflowId` with `Workflow.Id`. That is how the database knows which workflow owns the version.

Without the composite key, this would be possible:

```text
Version V4: WorkspaceId = WS-B, WorkflowId = W1
Workflow W1: WorkspaceId = WS-A
```

A simple foreign key on `WorkflowId` alone would accept `V4` because `W1` exists. The composite key rejects it because `(WS-B, W1)` does not exist in `Workflows`.

### 6.2 Active Version Belongs to the Same Workflow

Configuration:

```csharp
.HasForeignKey(w => new { w.Id, w.ActiveVersionId })
.HasPrincipalKey(v => new { v.WorkflowId, v.Id })
```

| Position | Workflow column | WorkflowVersion column |
| :---: | :--- | :--- |
| 1 | `Id` | `WorkflowId` |
| 2 | `ActiveVersionId` | `Id` |

The first column is the workflow's own primary key. It is not a separate `ActiveVersionWorkflowId` column. It lets the database compare:

```text
Workflow.Id == WorkflowVersion.WorkflowId
```

and

```text
Workflow.ActiveVersionId == WorkflowVersion.Id
```

at the same time.

Because `ActiveVersionId` is nullable, the foreign key is optional. If it is `NULL`, no check is performed. If it is set, both comparisons must match.

The active-version foreign key does not include `WorkspaceId` directly. It does not need to: the version's own `(WorkspaceId, WorkflowId)` foreign key already guarantees that the version and its workflow share a workspace.

### 6.3 Why Alternate Keys Are Required

A foreign key must point to a **unique** set of columns. The primary keys are only `Id`, so the composite foreign keys need additional unique targets:

| Referencing table | Foreign key | Referenced table | Required unique target |
| :--- | :--- | :--- | :--- |
| `WorkflowVersion` | `(WorkspaceId, WorkflowId)` | `Workflow` | `(WorkspaceId, Id)` alternate key |
| `Workflow` | `(Id, ActiveVersionId)` | `WorkflowVersion` | `(WorkflowId, Id)` alternate key |

`HasAlternateKey` creates those unique constraints. They are redundant for row identity because `Id` is already unique, but they are necessary as foreign key targets.

---

## 7. How the Two Configurations Work Together

The two relationships form a cycle:

```text
Workflow ── owns ──> WorkflowVersion
    ^                      │
    └── ActiveVersionId ───┘
```

This cycle affects insert and delete order.

### 7.1 Safe Insert Sequence

A new workflow starts with `ActiveVersionId = NULL`, which breaks the cycle. A safe sequence is:

```csharp
var workflow = Workflow.Create(
    workflowId,
    workspaceId,
    userId,
    "Sync CRM",
    "Imports contacts from the CRM.",
    clock);

var version = WorkflowVersion.CreateDraft(
    versionId,
    workspaceId,
    workflow.Id,
    versionNumber: 1,
    definition,
    validator,
    clock);

version.Publish(clock);
workflow.ActivateVersion(version, clock);
workflow.Activate(clock);
```

When persisting with EF Core, save in this order:

```csharp
db.Workflows.Add(workflow);
await db.SaveChangesAsync();
// Workflows row exists with ActiveVersionId = NULL.

db.WorkflowVersions.Add(version);
await db.SaveChangesAsync();
// WorkflowVersions row exists and satisfies (WorkspaceId, WorkflowId).

workflow.ActivateVersion(version, clock);
await db.SaveChangesAsync();
// Workflows.ActiveVersionId is now set and satisfies (Id, ActiveVersionId).
```

Why this order?

1. The workflow must exist before the version can reference it.
2. The version must exist before the workflow can select it.
3. The first workflow insert leaves `ActiveVersionId` as `NULL`, so there is no circular dependency during the first save.

It is possible to add the workflow and version to the same `SaveChanges` call because the version depends on the workflow and the active version is still `NULL`. The explicit three-step sequence is easier to reason about and avoids save-order surprises.

### 7.2 Safe Delete Sequence

Because `WorkflowVersion → Workflow` uses `Cascade`, deleting a workflow can delete its versions. However, `Workflow.ActiveVersionId → WorkflowVersion` uses `Restrict`, so a version that is currently selected cannot be deleted directly.

A predictable deletion sequence is shown below. It is illustrative: `ClearActiveVersion` does not exist on `Workflow` yet and would need to set `ActiveVersionId = null`.

```csharp
workflow.Deactivate(clock);
workflow.ClearActiveVersion(clock); // Not implemented yet.
await db.SaveChangesAsync();

db.Workflows.Remove(workflow);
await db.SaveChangesAsync();
// Database cascade removes the workflow's versions.
```

Until `ClearActiveVersion` is added, deletion of a workflow with an active version may be blocked or may depend on database trigger ordering. Add an integration test for this behavior before relying on it.

A published version that is not selected as an active version can also be deleted directly; publication immutability is not enforced by the current database configuration. It is planned as an application-level interceptor, not a database trigger.

### 7.3 Domain Checks vs Database Checks

The database and the domain protect different things:

| Rule | Enforced by database? | Enforced by domain? |
| :--- | :---: | :---: |
| Version belongs to the same workflow | Yes, composite FK | Yes, `ActivateVersion` checks `WorkflowId` |
| Version belongs to the same workspace | Yes, composite FK | Yes, `ActivateVersion` checks `WorkspaceId` |
| Version number is positive | Not yet | Yes, `CreateDraft` rejects `<= 0` |
| Version number is unique per workflow | Yes, unique index | Not applicable |
| Workflow name is unique per workspace | Yes, unique index | Not applicable |
| Active version is published | No | Yes, `ActivateVersion` rejects drafts |
| Published version has no validation errors | Not yet | Yes, `Publish` rejects them |
| Published version has at least one step | Not yet | Yes, `Publish` rejects empty definitions |
| Published versions are immutable | Not yet | Yes, `Publish` is one-way and properties are private-set |

This is why both layers matter. The database protects ownership and uniqueness; the domain protects workflow lifecycle rules.

---

## 8. Multi-Tenancy and Write Protection

Both `Workflow` and `WorkflowVersion` implement `IMustHaveWorkspace`. `ApplicationDbContext.ApplyWorkspaceFilters` automatically adds a global query filter to every such entity:

```csharp
modelBuilder.Entity<TEntity>()
    .HasQueryFilter(e => e.WorkspaceId == _workspaceContext.CurrentWorkspaceId);
```

A normal query such as:

```csharp
var workflows = await db.Workflows.ToListAsync();
```

is translated to SQL similar to:

```sql
SELECT ...
FROM "Workflows"
WHERE "WorkspaceId" = @currentWorkspaceId;
```

If no workspace is selected, `CurrentWorkspaceId` is `NULL`. The comparison evaluates to false, so the query returns zero rows. This is the **fail-closed** read behavior.

> [!IMPORTANT]
> Global query filters protect reads, not writes. Inserts, updates, and deletes are not filtered by `HasQueryFilter`.
>
> Write-side protection comes from:
> 1. Domain methods that check `WorkspaceId` and workflow ownership.
> 2. Database composite foreign keys that reject cross-workspace references.
> 3. `TenantWriteValidationInterceptor` for application write paths.
>
> Direct SQL can still bypass application-level rules. Database constraints are the last line of defense.

The controlled bypass for bootstrap or administrative code is:

```csharp
var allWorkflows = await db.Workflows
    .IgnoreQueryFilters()
    .ToListAsync();
```

Use it only where a deliberate cross-workspace view is required, and never as a shortcut in normal request handling.

---

## 9. Design Decisions and Trade-offs

| Decision | Alternative | Why This Was Chosen | Trade-off |
| :--- | :--- | :--- | :--- |
| Store the definition as one JSON snapshot | Separate `WorkflowSteps` and `StepDependencies` tables | Versions are immutable snapshots; one row preserves exactly what was published and simplifies reads | Individual steps cannot be joined or updated relationally; JSON operators are needed for deep queries |
| Use PostgreSQL `jsonb` | Plain `text` | PostgreSQL validates JSON, stores it efficiently, and supports JSON queries and indexes | Requires PostgreSQL-specific mapping; less portable to other databases |
| Composite FK for version ownership | Simple `WorkflowId` FK | Prevents a version from belonging to a workflow in another workspace | More complex mapping and an extra alternate key |
| Composite FK for active version | Simple `ActiveVersionId` FK | Prevents selecting a version that belongs to a different workflow | Circular FK and more complex insert/delete ordering |
| Alternate keys on `(WorkspaceId, Id)` and `(WorkflowId, Id)` | No alternate keys | Composite foreign keys require unique principal keys | Redundant unique constraints in addition to the primary keys |
| Unique workflow name per workspace | No name uniqueness | Prevents confusing duplicate names for users | Renames can conflict; application must handle unique-violation errors |
| Unique version number per workflow | Unique version number globally | Version `1` naturally exists in every workflow | Duplicate numbers within one workflow must be handled by the caller |
| `Restrict` for workspace and user FKs | `Cascade` | Prevents accidental deletion of tenant data and audit history | Deletion requires explicit cleanup or archival |
| `Cascade` for workflow → versions | `Restrict` | Versions are owned by the workflow; cleanup is automatic | Can conflict with the active-version `Restrict` FK; deletion order matters |
| Nullable `ValidationErrorsJson` | Always store an empty array | SQL `NULL` unambiguously means "valid"; an empty array could be confused with "invalid but no errors" | Requires null checks in application code |
| `IsActive` default `false` | Default `true` | New workflows must be deliberately activated | A workflow inserted directly with no value will not run |
| No navigation properties | EF navigation properties across modules | Keeps domain entities and modules decoupled; foreign keys are still enforced | No eager loading through navigation properties; queries must be explicit |
| `DefinitionSchemaVersion` separate from `VersionNumber` | Reuse `VersionNumber` | The JSON shape can evolve independently of the business version | Callers must store and check both values |

---

## 10. Examples and Verification

### 10.1 Example Dataset

Assume these rows exist:

**Workspaces**

| Id | Name |
| :--- | :--- |
| `WS-A` | Team A |
| `WS-B` | Team B |

**Workflows**

| Id | WorkspaceId | Name | ActiveVersionId |
| :--- | :--- | :--- | :--- |
| `W1` | `WS-A` | `Sync CRM` | `V1` |
| `W2` | `WS-A` | `Sync ERP` | `V3` |
| `W3` | `WS-B` | `Sync CRM` | `NULL` |

**WorkflowVersions**

| Id | WorkspaceId | WorkflowId | VersionNumber | IsPublished | PublishedAt | ValidationErrorsJson |
| :--- | :--- | :--- | :---: | :---: | :--- | :--- |
| `V1` | `WS-A` | `W1` | 1 | true | `2026-10-01T10:00:00Z` | `NULL` |
| `V2` | `WS-A` | `W1` | 2 | false | `NULL` | `NULL` |
| `V3` | `WS-A` | `W2` | 1 | true | `2026-10-02T09:30:00Z` | `NULL` |
| `V4` | `WS-B` | `W3` | 1 | false | `NULL` | `["Step step-a depends on unknown step missing-step."]` |

### 10.2 Allowed and Rejected Operations

| Operation | Result | Reason |
| :--- | :--- | :--- |
| Set `W1.ActiveVersionId = V1` | Allowed | `V1` belongs to `W1` and is published. |
| Set `W1.ActiveVersionId = V2` | Allowed by database, rejected by domain | `V2` belongs to `W1`, but it is a draft. The FK checks ownership; `ActivateVersion` checks publication. |
| Set `W1.ActiveVersionId = V3` | Rejected by database | `V3` belongs to `W2`, not `W1`. |
| Insert `V4` with `WorkspaceId = WS-B`, `WorkflowId = W1` | Rejected by database | `(WS-B, W1)` does not match `W1`'s `(WS-A, W1)`. |
| Insert another `W1` version with `VersionNumber = 1` | Rejected by unique index | Version numbers must be unique per workflow. |
| Insert a `W2` version with `VersionNumber = 1` | Allowed | Version numbers are scoped per workflow. |
| Insert another `WS-A` workflow named `Sync CRM` | Rejected by unique index | Workflow names must be unique per workspace. |
| Insert a `WS-B` workflow named `Sync CRM` | Allowed | The name is unique within `WS-B`. |
| Delete `WS-A` | Rejected | Workflows and versions still reference it; the workspace FK uses `Restrict`. |
| Delete user who created `W1` | Rejected | The creator FK uses `Restrict`. |
| Delete `W1` | Cascade removes its versions | The workflow → version FK uses `Cascade`; the active-version FK may require clearing `ActiveVersionId` first. |

### 10.3 Illustrative SQL Shape

EF Core generates the actual constraint names. Because the two tables reference each other, the tables are created first and the active-version foreign key is added afterward. The shape is:

```sql
CREATE TABLE "Workflows" (
    "Id" uuid NOT NULL,
    "WorkspaceId" uuid NOT NULL,
    "CreatedByUserId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Description" character varying(2000),
    "IsActive" boolean NOT NULL DEFAULT FALSE,
    "ActiveVersionId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_Workflows" PRIMARY KEY ("Id"),
    CONSTRAINT "AK_Workflows_WorkspaceId_Id" UNIQUE ("WorkspaceId", "Id"),
    CONSTRAINT "FK_Workflows_Workspaces_WorkspaceId"
        FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Workflows_AspNetUsers_CreatedByUserId"
        FOREIGN KEY ("CreatedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_Workflows_WorkspaceId_Name"
    ON "Workflows" ("WorkspaceId", "Name");

CREATE TABLE "WorkflowVersions" (
    "Id" uuid NOT NULL,
    "WorkspaceId" uuid NOT NULL,
    "WorkflowId" uuid NOT NULL,
    "VersionNumber" integer NOT NULL,
    "DefinitionSchemaVersion" integer NOT NULL,
    "IsPublished" boolean NOT NULL,
    "SerializedDefinition" jsonb NOT NULL,
    "ValidationErrorsJson" jsonb,
    "PublishedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_WorkflowVersions" PRIMARY KEY ("Id"),
    CONSTRAINT "AK_WorkflowVersions_WorkflowId_Id" UNIQUE ("WorkflowId", "Id"),
    CONSTRAINT "FK_WorkflowVersions_Workspaces_WorkspaceId"
        FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_WorkflowVersions_Workflows_WorkspaceId_WorkflowId"
        FOREIGN KEY ("WorkspaceId", "WorkflowId")
        REFERENCES "Workflows" ("WorkspaceId", "Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_WorkflowVersions_WorkflowId_VersionNumber"
    ON "WorkflowVersions" ("WorkflowId", "VersionNumber");

ALTER TABLE "Workflows"
    ADD CONSTRAINT "FK_Workflows_WorkflowVersions_Id_ActiveVersionId"
    FOREIGN KEY ("Id", "ActiveVersionId")
    REFERENCES "WorkflowVersions" ("WorkflowId", "Id") ON DELETE RESTRICT;
```

> [!NOTE]
> This SQL is illustrative. Generate the real migration before relying on exact names, column types, or delete behavior.

### 10.4 Query Filter Example

With a workspace context set to `WS-A`:

```csharp
var workflows = await db.Workflows.ToListAsync();
// Returns W1 and W2, not W3.

var versions = await db.WorkflowVersions.ToListAsync();
// Returns V1, V2, and V3, not V4.
```

With no workspace context:

```csharp
// CurrentWorkspaceId is NULL.
var workflows = await db.Workflows.ToListAsync();
// Returns zero rows (fail-closed).
```

