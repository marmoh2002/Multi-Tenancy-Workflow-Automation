# Workflow Core Tests

This directory contains unit tests for the core workflow domain logic and serialization rules in `WorkflowAutomation.Workflows.Core`.

---

## Test Suites

| Test Suite | Responsibility | Primary Behaviors Tested |
| :--- | :--- | :--- |
| `DagValidatorTests` | Graph topology & execution order | Kahn's algorithm, cycle rejection, self-loops, dangling/unknown dependencies, topological ordering, and identifier constraints (trimmed, non-empty, max 100 chars on step IDs and dependency references). |
| `WorkflowDefinitionSerializerTests` | Serialization contract & data integrity | Schema versioning, structural invariants, nullability guards, string parameter enforcement, snapshot isolation. |

---

## Workflow Definition Serializer Rules

`WorkflowDefinitionSerializer` enforces strict structural validation before persisting or restoring workflow JSON definitions.

### 1. Schema Versioning
- **Current Version:** `1` (`WorkflowDefinitionSerializer.CurrentSchemaVersion`).
- Deserialization requires matching `schemaVersion == 1`.
- Any other version (e.g., `0` or `2`) throws `NotSupportedException`.

### 2. Root Structure
- The root must be a JSON object (`{ ... }`).
- Scalar roots (`"string"`, `123`, `true`), array roots (`[]`), and JSON `null` throw `JsonException`.
- The `"steps"` property is mandatory (`{}` is rejected with `JsonException`).
- An empty steps array (`{"steps": []}`) is valid as a draft definition.

> [!NOTE]
> **Why `{"steps": []}` is allowed but `{}` is rejected:**
> "Explicitly empty" and "missing required information" represent fundamentally different situations:
> - **`{"steps": []}` (explicitly empty):** Expresses explicit domain intent: *"This workflow has a steps collection, and it currently contains zero steps."* This is allowed as an in-progress draft (publication validation separately prevents publishing it).
> - **`{}` (missing contract):** The payload says nothing about steps. It could mean data was serialized incorrectly, sent by an incompatible schema version, or inadvertently omitted. We fail fast and reject it rather than silently assuming "no steps."


### 3. Step Invariants
- Array elements inside `"steps"` cannot be `null`.
- Each step must have a non-empty, non-whitespace `"id"`.
- Step `"id"` must be trimmed (leading or trailing whitespace is rejected with `JsonException`).
- Step `"id"` cannot exceed `StepDefinition.MaxIdLength` (100 characters).
- Step `"type"` cannot be `null`.

### 4. Dependency (`dependsOn`) Rules
- Optional in JSON: omitted `"dependsOn"` defaults to an empty collection (`[]`).
- Explicitly null `"dependsOn": null` is rejected (`JsonException`).
- Elements inside `"dependsOn"` cannot be `null`.
- Elements inside `"dependsOn"` cannot be empty or whitespace-only (`JsonException`).
- Elements inside `"dependsOn"` must be trimmed (leading or trailing whitespace is rejected with `JsonException`).
- Elements inside `"dependsOn"` cannot exceed `StepDefinition.MaxIdLength` (100 characters).

### 5. Parameter (`parameters`) Rules
- Optional in JSON: omitted `"parameters"` defaults to an empty dictionary (`{}`).
- Explicitly null `"parameters": null` is rejected (`JsonException`).
- All parameter values must be non-null strings (`IReadOnlyDictionary<string, string>`).
- Null values (`{"key": null}`), numbers (`42`), booleans (`true`), objects (`{}`), and arrays (`[]`) are rejected (`JsonException`).

### 6. In-Memory Validation (`Serialize`)
- `Serialize(null)` throws `ArgumentNullException`.
- In-memory definitions with null collections (`Steps = null`), null elements, blank or untrimmed IDs, IDs exceeding 100 characters, null/empty/untrimmed/overlong dependencies, or null parameter values throw `JsonException` before any JSON is generated.

### 7. Snapshot Behavior
- Deserialization always instantiates a completely fresh object graph.
- Returned instances (`WorkflowDefinition`, `Steps`, `StepDefinition`, `DependsOn`, `Parameters`) never share memory references with input objects or subsequent deserializations.

---

## Examples

### Valid Minimal Draft
```json
{
  "steps": []
}
```

### Valid Multi-Step Workflow with Dependencies and Parameters
```json
{
  "steps": [
    {
      "id": "fetch-data",
      "type": "HttpRequest",
      "parameters": {
        "url": "https://api.example.com/items",
        "method": "GET"
      }
    },
    {
      "id": "transform-data",
      "type": "DataTransform",
      "dependsOn": [
        "fetch-data"
      ],
      "parameters": {
        "format": "json"
      }
    }
  ]
}
```
*Note: In the payload above, `dependsOn` is omitted on `fetch-data` and correctly defaults to `[]`.*

### Invalid JSON Payloads (Rejected with `JsonException`)

| Invalid Payload | Violation Reason |
| :--- | :--- |
| `{}` | Missing mandatory `"steps"` property |
| `{"steps": null}` | `"steps"` must be a non-null array |
| `{"steps": [null]}` | Step entry cannot be null |
| `{"steps": [{"type": "Log"}]}` | Missing required `"id"` property |
| `{"steps": [{"id": "   ", "type": "Log"}]}` | Step ID cannot be blank or whitespace |
| `{"steps": [{"id": " step-1 ", "type": "Log"}]}` | Step ID cannot contain leading or trailing whitespace |
| `{"steps": [{"id": "<>100 chars>", "type": "Log"}]}` | Step ID cannot exceed 100 characters (`StepDefinition.MaxIdLength`) |
| `{"steps": [{"id": "s1", "dependsOn": null}]}` | `"dependsOn"` cannot be explicitly null |
| `{"steps": [{"id": "s1", "dependsOn": [null]}]}` | Dependency entry inside array cannot be null |
| `{"steps": [{"id": "s1", "dependsOn": ["   "]}]}` | Dependency reference cannot be empty or whitespace |
| `{"steps": [{"id": "s1", "dependsOn": [" step-0 "]}]}` | Dependency reference cannot contain leading or trailing whitespace |
| `{"steps": [{"id": "s1", "dependsOn": ["<>100 chars>"]}]}` | Dependency reference cannot exceed 100 characters (`StepDefinition.MaxIdLength`) |
| `{"steps": [{"id": "s1", "parameters": null}]}` | `"parameters"` cannot be explicitly null |
| `{"steps": [{"id": "s1", "parameters": {"key": null}}]}` | Parameter value cannot be null |
| `{"steps": [{"id": "s1", "parameters": {"timeout": 30}}]}` | Parameter values must be strings (not numbers) |

---

## Running Tests

Run only the core workflow unit tests:
```bash
dotnet test tests/WorkflowAutomation.Workflows.Core.Tests/
```

Run all tests in the solution:
```bash
dotnet test
```
