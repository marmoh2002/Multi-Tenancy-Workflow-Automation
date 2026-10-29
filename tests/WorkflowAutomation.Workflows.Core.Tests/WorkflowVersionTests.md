# WorkflowVersion Entity Tests

Unit tests for the `WorkflowVersion` entity in `WorkflowAutomation.Workflows.Core.Entities`. They verify draft snapshot creation, schema and DAG validation error recording, immutability, argument guards, and publication lifecycle rules.

---

## Test Infrastructure

| Component | Purpose |
| :--- | :--- |
| `TestClock` | Deterministic `IClock` implementation. Defaults to `2026-01-01T12:00:00Z`; `Advance(TimeSpan)` advances time to verify state mutation timestamps. |
| `KahnDagValidator` | Concrete `IDagValidator` using Kahn's algorithm for dependency ordering and cycle detection. |
| `WorkspaceId`, `WorkflowId` | Fixed static `Guid` values shared across tests. |
| `CreateSampleDefinition(...)` | Factory helper constructing a minimal single-step (`Log`) definition. |

---

## Coverage Summary

### 1. Draft Creation (`WorkflowVersion.CreateDraft`)

| Test | Verified Behavior |
| :--- | :--- |
| `CreateDraft_with_valid_definition_creates_unpublished_draft` | Correctly maps `Id`, `WorkspaceId`, `WorkflowId`, `VersionNumber`, and schema version (1); `IsPublished` is `false`; `PublishedAt` and `ValidationErrorsJson` are `null`; `CreatedAt` is set to clock; `UpdatedAt` is `null`; round-trip deserialization via `GetDefinition()` reproduces original steps. |
| `CreateDraft_rejects_empty_ids` | An empty `Guid` for `id`, `workspaceId`, or `workflowId` throws `ArgumentException` (three theory cases). |
| `CreateDraft_rejects_non_positive_version_number` | Version numbers `<= 0` (e.g., `0`, `-1`, `-100`) throw `ArgumentOutOfRangeException`. |
| `CreateDraft_rejects_null_definition` | A `null` definition throws `ArgumentNullException`. |
| `CreateDraft_rejects_null_validator` | A `null` DAG validator throws `ArgumentNullException`. |
| `CreateDraft_rejects_null_clock` | A `null` clock throws `ArgumentNullException`. |

### 2. DAG Error Capture

| Test | Verified Behavior |
| :--- | :--- |
| `CreateDraft_with_cycle_records_validation_errors_in_json` | Definitions containing cycles (e.g., `a -> b -> a`) do not throw during draft creation; `ValidationErrorsJson` captures the error message containing `"Cycle detected"`; `IsPublished` remains `false`. |
| `CreateDraft_with_missing_dependency_records_validation_errors_in_json` | Definitions referencing unknown steps do not throw during draft creation; `ValidationErrorsJson` captures the error message; `IsPublished` remains `false`. |

### 3. Publication (`WorkflowVersion.Publish`)

| Test | Verified Behavior |
| :--- | :--- |
| `Publish_succeeds_and_sets_published_and_updated_timestamps` | Valid draft transitions `IsPublished` to `true`; `PublishedAt` and `UpdatedAt` are set to the current clock timestamp. |
| `Publish_rejects_already_published_version` | Publishing an already-published version throws `InvalidOperationException` containing `"already published"`. |
| `Publish_rejects_version_with_validation_errors` | Attempting to publish a version with non-null `ValidationErrorsJson` throws `InvalidOperationException` containing `"validation errors"`. |
| `Publish_rejects_empty_workflow_definition` | Attempting to publish an empty definition (0 steps) throws `InvalidOperationException` containing `"empty workflow definition"`. |
| `Publish_rejects_null_clock` | Passing a `null` clock throws `ArgumentNullException`. |

---

## Domain Rules Under Test

- **Snapshot Isolation:** In-memory `WorkflowDefinition` models are immediately serialized to JSON upon draft creation. Subsequent in-memory mutations to the input object do not corrupt the version.
- **Permissive Draft Persistence:** Drafts with broken topologies or missing dependencies are persisted so users can work iteratively. Errors are stored as a JSON string in `ValidationErrorsJson`.
- **Identity Invariants:** `Id`, `WorkspaceId`, and `WorkflowId` must all be valid non-empty GUIDs.
- **Positive Versioning:** Version numbers must be positive integers (`> 0`).
- **Publication Gate:** Publication is strict:
  1. No validation errors allowed (`ValidationErrorsJson is null`).
  2. At least one executable step required (`definition.Steps.Count > 0`).
- **One-Way Publication:** Once `IsPublished == true`, a version is permanently sealed. It cannot be edited or republished.
- **Clock-Controlled Timestamps:** `CreatedAt` records initial draft creation; `PublishedAt` and `UpdatedAt` record publication.

---

## Related Tests

- [WorkflowTests.md](WorkflowTests.md): Tests the aggregate root (`Workflow`), verifying version selection (`ActivateVersion`) and activation guards.
- [WorkflowDefinitionSerializerTests.md](WorkflowDefinitionSerializerTests.md): Tests schema validation, step structure, and JSON serialization.

---

## Running the Tests

Run all unit tests in the project:
```bash
dotnet test tests/WorkflowAutomation.Workflows.Core.Tests/
```

Run only this suite:
```bash
dotnet test tests/WorkflowAutomation.Workflows.Core.Tests/ --filter FullyQualifiedName~WorkflowVersionTests
```
