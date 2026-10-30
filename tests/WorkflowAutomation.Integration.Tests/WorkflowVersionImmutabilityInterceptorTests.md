# WorkflowVersionImmutabilityInterceptor Integration Tests

Integration test suite for `WorkflowVersionImmutabilityInterceptor`, verifying write-boundary immutability rules, lifecycle transitions, and deletion semantics enforced at the EF Core persistence layer.

---

## 1. Architectural Purpose & Defense-in-Depth

The `WorkflowVersion` domain entity encapsulates business rules through its methods (e.g., `UpdateDraft()`, `Publish()`), rejecting illegal mutations in memory. However, domain logic alone does not protect against:
- Direct mutation of entity properties via EF Core ChangeTracker (`db.Entry(version).Property(...).CurrentValue = ...`).
- Accidental or malicious deletion via `db.WorkflowVersions.Remove(...)`.
- Unintended state transitions triggered outside domain methods.

`WorkflowVersionImmutabilityInterceptor` sits at the **EF Core persistence boundary** (`SaveChangesInterceptor`), intercepting all additions, modifications, and deletions before SQL statements are dispatched to PostgreSQL.

---

## 2. Test Coverage & Scenarios

| # | Test Scenario | Category | Key Verification |
|---|---|---|---|
| 1 | `GivenValidDraft_WhenDraftUpdatedAndSaved_SucceedsAndPersistsChanges` | Draft Mutation (Happy Path) | Updating a draft's serialized definition persists changes across distinct `DbContext` scopes. |
| 2 | `GivenDraft_WhenPublishedAndSaved_SucceedsAndPersistsPublication` | Publication (Happy Path) | Transitioning from draft to published via `Publish()` persists `IsPublished = true` and `PublishedAt`. |
| 3 | `GivenPublishedVersion_WhenModified_ThrowsInvalidOperationException` | Immutability (Negative) | Bypassing domain methods to modify properties of an already published version throws `"Cannot modify or unpublish an already published workflow version"`. |
| 4 | `GivenDraft_WhenImmutablePropertyModified_ThrowsInvalidOperationException` | Immutability (Negative) | Mutating permanent immutable metadata (`VersionNumber`, `WorkflowId`, `WorkspaceId`, etc.) on a draft throws `"Cannot modify immutable property 'VersionNumber' on workflow version"`. |
| 5 | `GivenDraft_WhenDeletedIndependently_Succeeds` | Deletion (Happy Path) | Discarding an unpublished draft version independently via `db.WorkflowVersions.Remove(draft)` succeeds. |
| 6 | `GivenPublishedVersion_WhenDeletedIndependently_ThrowsInvalidOperationException` | Deletion (Negative) | Removing an existing published version independently throws `"Cannot delete a published workflow version independently"`. |
| 7 | `GivenPublishedVersion_WhenDeletedAlongWithOwningWorkflow_Succeeds` | Cascade Deletion (Happy Path) | Deleting a published version alongside its owning `Workflow` in the same unit of work is permitted and succeeds. |

---

## 3. Detailed Rule Verifications

### 3.1 Draft Updates & Permitted Mutability
- **Permitted Draft Properties:** When `entry.Entity.IsPublished == false`, only properties in `DraftEditableProperties` (`SerializedDefinition`, `DefinitionSchemaVersion`, `ValidationErrorsJson`, `UpdatedAt`) are allowed to change.
- **Permanent Immutable Properties:** Properties in `ImmutableProperties` (`Id`, `WorkspaceId`, `WorkflowId`, `VersionNumber`, `CreatedAt`) cannot be modified on either drafts or published versions.

### 3.2 Publication Lifecycle Transition
- During transition (`OriginalValue(IsPublished) == false` and `CurrentValue(IsPublished) == true`):
  - `PublishedAt` must be populated.
  - `ValidationErrorsJson` must be `null`.
  - Only publication-related and draft-editable properties may be modified simultaneously.

### 3.3 Published Version Immutability
- If `OriginalValue(IsPublished) == true`:
  - No application properties may be modified (concurrency tokens are excluded).
  - Unpublishing a published version is strictly rejected.

### 3.4 Deletion Constraints
- **Unpublished Drafts:** Can be discarded independently at any time (`wasPublished == false`).
- **Published Versions:** Prohibited from standalone deletion. Deletion is permitted **only** when the owning `Workflow` (`WorkspaceId`, `WorkflowId`) is tracked as `EntityState.Deleted` within the exact same `ChangeTracker` unit of work.

---

## 4. Test Infrastructure & Execution

- **Database:** PostgreSQL container hosted via `Testcontainers` (`PostgreSqlFixture`).
- **Multi-Tenancy:** Each test executes within an isolated DI service scope where `WorkspaceContext` is explicitly initialized before querying or saving.
- **Scope Isolation:** Tests follow a 3-step pattern:
  1. *Seed:* Initialize prerequisites (`Workspace`, `Workflow`, `WorkflowVersion`) in Scope 1.
  2. *Act:* Load the entity and perform the action under test in Scope 2.
  3. *Assert:* Load and verify persisted state in a fresh Scope 3 (or catch exceptions on `SaveChangesAsync()`).

To run the full suite:
```bash
dotnet test tests/WorkflowAutomation.Integration.Tests/ --filter FullyQualifiedName~WorkflowVersionImmutabilityInterceptorTests
```
