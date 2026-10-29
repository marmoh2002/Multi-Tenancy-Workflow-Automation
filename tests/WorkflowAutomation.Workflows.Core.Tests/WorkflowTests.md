# Workflow Entity Tests

Unit tests for the `Workflow` aggregate root in `WorkflowAutomation.Workflows.Core.Entities`. They verify creation invariants, editable details, lifecycle timestamps, and the selection/activation of published `WorkflowVersion` instances.

---

## Test Infrastructure

| Component | Purpose |
| :--- | :--- |
| `TestClock` | Deterministic `IClock` implementation. Defaults to `2026-01-01T12:00:00Z`; `Advance(TimeSpan)` moves time forward to prove timestamp changes. |
| `WorkspaceId`, `CreatorId` | Fixed static `Guid` values shared across tests. |
| `CreateDraftVersion(...)` | Builds a valid one-step (`Log`) definition and creates a draft `WorkflowVersion` for the supplied workflow/workspace. |
| `CreatePublishedVersion(...)` | Creates a draft and calls `Publish(_clock)`, yielding a version eligible for selection. |

---

## Coverage Summary

### 1. Creation (`Workflow.Create`)

| Test | Verified Behavior |
| :--- | :--- |
| `Successful_creation_starts_inactive_without_active_version` | All constructor inputs are mapped (`Id`, `WorkspaceId`, `CreatedByUserId`, `Name`, `Description`); `IsActive` is `false`; `ActiveVersionId` is `null`; `CreatedAt` equals the clock value; `UpdatedAt` is `null`. |
| `Create_trims_name_and_description` | Leading/trailing whitespace is removed from both `Name` and `Description`. |
| `Create_rejects_empty_ids` | An empty `Guid` for the workflow, workspace, or creator throws `ArgumentException` (three theory cases). |
| `Create_rejects_missing_or_whitespace_name` | `null`, `""`, and `"   "` names throw `ArgumentException`. |
| `Create_rejects_overlong_name` | A 201-character name throws `ArgumentException` (maximum is 200). |
| `Create_rejects_overlong_description` | A 2001-character description throws `ArgumentException` (maximum is 2000). |
| `Create_rejects_null_clock` | A `null` clock throws `ArgumentNullException`. |

### 2. Detail Updates (`Workflow.UpdateDetails`)

| Test | Verified Behavior |
| :--- | :--- |
| `UpdateDetails_updates_name_and_description_and_sets_updated_at` | New values are trimmed and persisted; `UpdatedAt` is set to the current clock value. |
| `UpdateDetails_with_same_values_is_noop_and_preserves_timestamp` | Values that trim to the existing state do not modify the entity; `UpdatedAt` remains `null`. |
| `UpdateDetails_rejects_missing_or_whitespace_name` | `null`, `""`, and `"   "` names throw `ArgumentException`. |
| `UpdateDetails_rejects_overlong_name` | A 201-character name throws `ArgumentException`. |
| `UpdateDetails_rejects_overlong_description` | A 2001-character description throws `ArgumentException`. |
| `UpdateDetails_rejects_null_clock` | A `null` clock throws `ArgumentNullException` even when the values themselves are valid. |

### 3. Workflow Activation (`Activate` / `Deactivate`)

| Test | Verified Behavior |
| :--- | :--- |
| `Activate_without_selected_version_is_rejected` | Throws `InvalidOperationException` containing `"without a selected active version"`; `IsActive` stays `false`. |
| `Deactivate_when_already_inactive_is_noop_and_preserves_timestamp` | Deactivating an inactive workflow changes nothing and leaves `UpdatedAt` `null`. |
| `Version_selection_and_activation_lifecycle_succeeds` | Full lifecycle: selecting a published version sets `ActiveVersionId` while leaving the workflow inactive; `Activate` flips `IsActive` to `true`; `Deactivate` flips it back to `false`. Each real transition advances `UpdatedAt`. |
| `Activate_when_already_active_is_noop_and_preserves_timestamp` | Calling `Activate` on an already active workflow is a no-op; `UpdatedAt` is unchanged. |

### 4. Version Selection (`ActivateVersion`)

| Test | Verified Behavior |
| :--- | :--- |
| `ActivateVersion_rejects_unpublished_version` | A draft version throws `InvalidOperationException` containing `"Cannot activate an unpublished version"`; `ActiveVersionId` stays `null`. |
| `ActivateVersion_rejects_version_from_different_workflow` | A version belonging to another workflow throws `InvalidOperationException` containing `"belongs to a different workflow"`; `ActiveVersionId` stays `null`. |
| `ActivateVersion_rejects_version_from_different_workspace` | A version with a mismatched workspace throws `InvalidOperationException` containing `"belongs to a different workspace"`; `ActiveVersionId` stays `null`. |
| `ActivateVersion_with_same_active_version_is_noop_and_preserves_timestamp` | Re-selecting the current active version is a no-op; `UpdatedAt` is unchanged. |

---

## Domain Rules Under Test

- **Identity is required:** workflow, workspace, and creator IDs must all be non-empty.
- **Name constraints:** required, non-whitespace, trimmed, maximum 200 characters.
- **Description constraints:** optional, trimmed when present, maximum 2000 characters.
- **Timestamps are controlled by the clock:** `CreatedAt` is set once on creation; `UpdatedAt` is set only by genuine state transitions.
- **Version selection precedes activation:** `Activate` requires a non-null `ActiveVersionId`.
- **Version provenance:** a version must match both `WorkflowId` and `WorkspaceId`.
- **Publication gate:** only published versions may be selected.
- **Idempotent transitions:** re-selecting the same version, re-activating an active workflow, and deactivating an inactive workflow are all no-ops that preserve `UpdatedAt`.

### Guard Evaluation Order in `ActivateVersion`

1. Null checks on `version` and `clock` (`ArgumentNullException`).
2. Workflow ID mismatch (`InvalidOperationException`).
3. Workspace ID mismatch (`InvalidOperationException`).
4. Unpublished version (`InvalidOperationException`).
5. Same-version no-op.

> [!NOTE]
> Because provenance checks run before the publication check, passing a draft version from another workflow reports the workflow mismatch rather than the publication error.

---

## Related Tests

For serialization and DAG validation behavior used by `CreateDraftVersion`, see [WorkflowDefinitionSerializerTests.md](WorkflowDefinitionSerializerTests.md).

---

## Running the Tests

```bash
dotnet test tests/WorkflowAutomation.Workflows.Core.Tests/
```

Run only this suite:

```bash
dotnet test tests/WorkflowAutomation.Workflows.Core.Tests/ --filter FullyQualifiedName~WorkflowTests
```
