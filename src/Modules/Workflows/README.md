# WorkflowAutomation.Workflows

The workflow module of the Workflow Automation platform. It owns workflow
definitions, workflow versions, executions, and the application use cases that
operate on them.

This README describes the module structure and the Phase 4 application
contracts. The authoritative implementation contract remains
`phase4_plan.md` in the workspace root.

---

## 1. Module boundaries

The platform is a modular monolith. Modules communicate through explicit
contracts, not through each other's internals.

- Workflows belong to a workspace identified by a raw `Guid`.
- No cross-module navigation properties are added.
- `Workflows.Application` is host-agnostic: it does not know whether it is
  called by an HTTP API, a worker, or a test.
- Workspace and caller identity come only from `IWorkspaceContext`. Request
  bodies never supply a trusted workspace ID.
- Role-based authorization is a Phase 5 API concern, not an Application-layer
  concern.

When the Identity or Workspaces modules gain their own use cases and persistence
adapters, they follow the same per-module pattern. Empty projects are not
created in advance.

---

## 2. Project layout

```text
src/Modules/Workflows/
├── WorkflowAutomation.Workflows.Core/            domain model and invariants
├── WorkflowAutomation.Workflows.Application/     use cases, contracts, orchestration
└── WorkflowAutomation.Workflows.Infrastructure/  EF Core adapters

tests/
├── WorkflowAutomation.Workflows.Core.Tests/          domain tests
├── WorkflowAutomation.Workflows.Application.Tests/   application unit tests
└── WorkflowAutomation.Integration.Tests/             PostgreSQL integration tests
```

### Responsibilities

| Project | Owns | Must not contain |
| :--- | :--- | :--- |
| `Workflows.Core` | Entities, value shapes, DAG validation, domain state transitions | MediatR, EF Core, transactions, DTOs, host concerns |
| `Workflows.Application` | Commands, queries, handlers, validators, pipeline behaviors, DTOs, error vocabulary, persistence ports | EF Core, Npgsql, `ApplicationDbContext`, ASP.NET Core, MassTransit |
| `Workflows.Infrastructure` | EF Core implementations of application ports, read projections, transaction wrapper, GUID generator | Business rules already enforced by the domain |
| `Workflows.Application.Tests` | Handler, validator, behavior, and architecture tests with fakes | Real database dependencies |
| `WorkflowAutomation.Integration.Tests` | End-to-end persistence behavior against PostgreSQL | Module-internal unit-test fakes |

### Dependency direction

```text
Host / API / Worker (future)
        │
        ▼
Workflows.Application  ──────►  Workflows.Core
        │                       Shared.Kernel
        │                       Shared.Contracts
        ▲
        │ implements its ports
Workflows.Infrastructure ────►  WorkflowAutomation.Infrastructure
                                (ApplicationDbContext, interceptors)
```

Dependencies point inward. Application defines what it needs; Infrastructure
supplies the implementations. Application never references Infrastructure.

### Why separate projects instead of folders

Folders do not enforce dependencies. A project is a compilation unit: if
`Workflows.Application` does not reference EF Core, a handler cannot use
`DbContext`. The architecture rule becomes a build error rather than a
convention someone can forget.

### When another module gets its own projects

Do not create empty projects for symmetry. A module gains a project when it has
the responsibility that project represents:

| Project | Create it when... |
| :--- | :--- |
| `Core` | The module has domain entities and invariants to enforce |
| `Application` | The module has a use case to orchestrate, or a host needs its contracts |
| `Infrastructure` | The module needs its own persistence adapters |
| `Application.Tests` | There is module-specific behavior worth unit testing |

Identity and Workspaces currently contain only their Core projects. They will
gain Application and Infrastructure projects when their phases introduce real
use cases and adapters. Creating them now would produce empty projects that
exist only to match a pattern.

### Direct package dependencies

| Project | Direct packages |
| :--- | :--- |
| `Workflows.Application` | `MediatR` 14.3.0, `FluentValidation` 12.2.0, `FluentValidation.DependencyInjectionExtensions` 12.2.0, DI and logging abstractions 10.0.13 |
| `Workflows.Infrastructure` | `Microsoft.EntityFrameworkCore` 10.0.13, `Npgsql` 10.0.3, DI abstractions 10.0.13 |
| `Workflows.Application.Tests` | `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `coverlet.collector` 6.0.4, DI and logging 10.0.13 |

A package is declared only where its types are used directly. A transitive
package is an implementation detail, not a dependency you can safely rely on.

---

## 3. Application contracts

The Application layer is built contracts-first. Contracts define the request
shape, the response shape, the failure vocabulary, and the persistence ports
before any handler logic exists.

| Contract group | Status |
| :--- | :--- |
| Messaging markers (`ICommand`, `IQuery`, `ITransactionalCommand`) | Implemented |
| `Unit` | Implemented |
| Read models and `PagedResult<T>` | Implemented |
| Error vocabulary | Implemented |
| Persistence ports (`IUnitOfWork`, `ITransaction`, `IGuidGenerator`, `IWorkflowRepository`, `IWorkflowQueries`, `IWorkflowVersionNumberAllocator`) | Implemented as interfaces |
| List query request contracts | Implemented |
| Application exceptions | Implemented |

“Implemented as interfaces” means the contract exists in Application. No
Infrastructure implementation exists yet; that comes after the contracts and
pipeline are complete.

### 3.1 Messaging markers

All commands and queries return `Result<TResponse>`, so expected failures never
escape as exceptions.

```csharp
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface ITransactionalCommand<TResponse> : ICommand<TResponse>;
```

- `ICommand<T>` changes state.
- `IQuery<T>` is read-only and never starts a transaction.
- `ITransactionalCommand<T>` opts a command into the shared transaction
  behavior.
- `CreateWorkflowVersionCommand` and `UpdateDraftWorkflowVersionCommand`
  deliberately do **not** implement `ITransactionalCommand<T>`. They own their
  transactions because they need rollback-and-retry or rollback-and-reload
  behavior.
- `Unit` represents a command with no useful return value, because C# has no
  `Result<void>`.

### 3.2 Read models

DTOs are immutable records with `required` and `init` properties. Handlers
never return domain entities.

| DTO | Purpose |
| :--- | :--- |
| `WorkflowSummaryDto` | Workflow list projection; includes active-version number |
| `WorkflowDetailsDto` | Full workflow details, including active-version publication time |
| `WorkflowVersionSummaryDto` | Version list without deserializing definitions |
| `WorkflowVersionDetailsDto` | Typed definition plus raw stored JSON and validation errors |
| `WorkflowExecutionSummaryDto` | Execution list without the full snapshot |
| `WorkflowExecutionDetailsDto` | Execution audit data, raw snapshot, and integrity flag |

`PagedResult<T>` carries `Items`, `Page`, `PageSize`, `TotalCount`, and a
computed `TotalPages`.

Important details:

- `RowVersion` is PostgreSQL `xmin` projected as `uint`. Phase 5 formats it as
  an `ETag`.
- `WorkflowVersionDetailsDto` returns both `Definition` (typed) and
  `SerializedDefinition` (raw `jsonb` text). The raw value is
  PostgreSQL-normalized, so it is not necessarily byte-identical to the
  client's original input.
- Execution DTOs keep the stored `WorkflowName` and `VersionNumber` separate
  from `WorkflowDisplayName` and `VersionDisplayName`. When the original
  workflow no longer exists, the display names append `" (Snapshot)"`. This is
  a read-time presentation rule; stored execution rows are never mutated.
- Execution details do not deserialize the snapshot. They return the raw
  snapshot and `IsSnapshotIntact`, which is computed from `DefinitionHash`, so
  corrupt historical rows remain inspectable.

### 3.3 Error vocabulary

Expected failures are returned as `Error` values, not thrown exceptions.

- `WorkflowErrors` provides typed factories with stable codes such as
  `Workflows.NotFound`, `Workflows.NameAlreadyExists`, and
  `Workflows.VersionAlreadyPublished`.
- `ValidationError` extends `Error` with field-level failures for
  FluentValidation.
- `DraftConflictError` extends `Error` with the current server-side draft state
  so Phase 5 can return a `409` that lets the client fork edits into a new
  version instead of losing them.

Draft conflicts use one stable code, `Workflows.DraftConflict`, and distinguish
the case through `ConflictReason`:

- `DraftModifiedByAnotherUser`
- `VersionPublishedByAnotherUser`

FluentValidation failures use the code `Validation.Failed`.

### 3.4 Persistence ports

Application defines the ports; Infrastructure implements them.

Implemented interfaces:

- `IUnitOfWork` — save changes, begin a transaction, clear the change tracker.
- `ITransaction` — commit, rollback, and track whether the transaction is
  completed.
- `IGuidGenerator` — deterministic ID generation in tests.
- `IWorkflowRepository` — tracked loads for write operations, add/remove,
  workflow row lock, and non-terminal-execution check.
- `IWorkflowQueries` — no-tracking read projections for queries.
- `IWorkflowVersionNumberAllocator` — per-workflow version-number allocation
  inside an active transaction.

`IWorkflowQueries` accepts the `ListWorkflowsQuery` and
`ListWorkflowExecutionsQuery` request contracts. These records define filtering
and pagination; their handlers are added later with the query implementations.

The repository and query split is deliberate:

- The write side loads tracked aggregates so handlers can call domain methods
  and save them.
- The read side uses no-tracking projections shaped directly into DTOs. Queries
  never mutate stored rows.

`IUnitOfWork.SaveChangesAsync` is more than a wrapper around EF Core. Its
contract includes translating provider exceptions:

- `DbUpdateConcurrencyException` → `ConcurrencyConflictException`
- PostgreSQL unique violation (`SqlState == "23505"`) →
  `UniqueConstraintViolationException`, with the provider constraint name
  mapped to a `UniqueConstraints` value.

This translation is what keeps EF Core and Npgsql types out of the Application
layer.

`UniqueConstraints` currently identifies:

- `IX_Workflows_WorkspaceId_Name` (`WorkflowName`)
- `IX_WorkflowVersions_WorkflowId_VersionNumber` (`WorkflowVersionNumber`)

Application-level exceptions:

- `ConcurrencyConflictException` — translated from EF Core's optimistic
  concurrency failure.
- `UniqueConstraintViolationException` — translated from a PostgreSQL unique
  violation and carries the mapped constraint name.
- `WorkflowDefinitionReadException` — raised when a stored definition cannot be
  deserialized; the query handler maps it to
  `Workflows.VersionDefinitionCorrupt`.

### 3.5 Why contracts come before handlers

A contract is the shape something must satisfy. A handler is an implementation
of that shape.

Defining contracts first means:

- Every handler has the same return type: `Result<TDto>`.
- Tests know what to assert before the logic exists.
- Infrastructure knows exactly which interfaces to implement.
- The compiler catches dependency mistakes while the change set is still small.
- Changing a handler signature does not ripple through every dependent class.

A successful build at this stage only proves the shapes fit together. It does
not mean a workflow operation can run. No handler, pipeline behavior, or
Infrastructure implementation exists yet.

### 3.6 Pipeline behaviors (planned)

Every request passes through an ordered MediatR pipeline:

| Order | Behavior | Responsibility | Applies to |
| :--- | :--- | :--- | :--- |
| 1 | `LoggingBehavior` | Log request name, outcome, duration, and correlation data. Never log payloads or secrets. | All requests |
| 2 | `WorkspaceContextBehavior` | Fail closed when no workspace context is available. | All requests |
| 3 | `ValidationBehavior` | Run FluentValidation validators and short-circuit with `ValidationError`. | Requests with validators |
| 4 | `TransactionBehavior` | Begin a transaction, commit on success, roll back on failure or exception. | `ITransactionalCommand<T>` only |

The order is deliberate:

- Logging should wrap everything, including validation failures.
- Workspace context must be established before validation or transaction work.
- Validation should run before a transaction is opened, so an invalid request
  never starts one.
- The transaction should be the closest behavior to the handler, so it wraps
  the actual state change.

Queries never start a transaction. `CreateWorkflowVersionCommand` and
`UpdateDraftWorkflowVersionCommand` skip the shared transaction behavior because
they control rollback, retry, and reload themselves.

### 3.7 Concurrency and conflict handling

`Workflow` and `WorkflowVersion` use PostgreSQL `xmin` as an optimistic
concurrency token. A client receives it as `RowVersion` in the DTOs.

When two callers save from the same token, the first succeeds and the second
fails with `DbUpdateConcurrencyException`. `IUnitOfWork.SaveChangesAsync`
translates that into `ConcurrencyConflictException`, so Application code never
references EF Core.

Handler policy:

- `UpdateDraftWorkflowVersionCommand` translates the conflict into a documented
  outcome.
- Every other command propagates it as an unexpected failure. Phase 5 maps that
  to a `500` response.

Draft conflict handling:

1. Roll back the aborted transaction. PostgreSQL marks a transaction aborted
   after a failed statement, so reading in it would also fail.
2. Clear the change tracker so the stale tracked entity cannot influence the
   reload.
3. Reload with `IWorkflowQueries` using no tracking.
4. Choose an outcome based on the reloaded state.

| Reloaded state | Outcome |
| :--- | :--- |
| Version deleted | `Workflows.NotFound` |
| Version published | `DraftConflictError` with `ConflictReason = VersionPublishedByAnotherUser` |
| Still a draft with a semantically equal definition and the same schema version | Convergent success with the reloaded DTO |
| Still a draft with a different definition or schema version | `DraftConflictError` with `ConflictReason = DraftModifiedByAnotherUser` |

Convergent-save comparison uses `JsonNode.DeepEquals`, not raw string equality.
PostgreSQL `jsonb` normalizes whitespace and key order, so the stored text is not
guaranteed to match the caller's original text byte-for-byte.

The system does not auto-merge divergent drafts. Merging two DAG edits can
produce a graph neither user reviewed. The fork path is
`CreateWorkflowVersionCommand`, which allocates the next version number and
preserves both users' work.

### 3.8 Version-number allocation

Version numbers are per workflow, positive, and allocated once at draft
creation:

```text
Begin transaction
    ↓
Lock the workflow row: SELECT 1 ... FOR UPDATE
    ↓
If the row is missing → NotFound
    ↓
SELECT COALESCE(MAX(VersionNumber), 0) + 1
    ↓
Create the draft version
    ↓
INSERT + SaveChanges + commit
```

Properties:

- The workflow row lock serializes concurrent allocations for that workflow.
- The unique index `(WorkflowId, VersionNumber)` remains the final guard.
- A unique violation triggers one retry with a fresh transaction and a fresh
  entity. A second failure returns `Workflows.VersionNumberAllocationConflict`.
- Gaps are allowed. Failed transactions may leave unused version numbers.
- Lock ordering is consistent: version creation and workflow deletion both
  lock the workflow row first, avoiding deadlocks.

### 3.9 Validation responsibilities

Validation is split by layer. No layer duplicates another layer's job.

| Layer | Responsibility |
| :--- | :--- |
| FluentValidation | Request shape: required IDs, string lengths, non-null definitions, supported schema version, pagination bounds, defined enum values |
| Domain (`Workflow`, `WorkflowVersion`) | Business invariants: ownership, publication, activation, deletion eligibility, name trimming, version positivity |
| `WorkflowDefinitionSerializer` | Structural JSON contract: object root, `steps` array, step IDs, dependencies, parameters |
| `KahnDagValidator` | Acyclicity, duplicate or missing dependencies, identifier invariants |
| Persistence interceptors | Allowed field changes, immutable ownership, publication consistency, deletion eligibility |

The Application layer validates request shape and translates documented domain
or serializer outcomes into typed errors. It does not re-implement domain rules.

### 3.10 Deletion and execution retention

`DeleteWorkflowCommand` follows this sequence:

1. Lock the workflow row.
2. Return `Workflows.WorkflowHasNonTerminalExecutions` if any execution is
   `Pending` or `Running`.
3. Delete the workflow. PostgreSQL cascades all `WorkflowVersion` rows,
   including published versions.
4. Retain all `WorkflowExecution` and `WorkflowStepExecution` rows.

There is no database foreign key from executions to workflows or versions. This
is intentional: terminal execution history must remain queryable after the
workflow is gone. The `(Snapshot)` suffix distinguishes those historical runs
from a later workflow that happens to reuse the same name.

Future execution creation must lock the same workflow row. That gives deletion a
reliable coordination point: either the execution is created before deletion
and then cascaded, or deletion wins and execution creation returns `NotFound`.

### 3.11 Glossary

| Term | Meaning |
| :--- | :--- |
| Command | A request that changes state and returns `Result<T>` |
| Query | A read-only request that returns `Result<T>` |
| Handler | The class that implements one command or query |
| DTO | An immutable read-model shape returned to the caller |
| Port | An interface defined by Application and implemented by Infrastructure |
| Adapter | An Infrastructure class that implements a port |
| Unit of Work | The abstraction that saves pending changes and controls transactions |
| Optimistic concurrency | Detecting a stale write by comparing a version token such as `xmin` |
| `xmin` | PostgreSQL's internal transaction ID, used here as a row version |
| Snapshot | The immutable workflow definition stored on an execution |
| Convergent save | A stale write whose content matches the current server state, so it succeeds instead of conflicting |

---

## 4. Request flow

```text
Host / future API / worker
        │
        ▼
MediatR command or query
        │
        ▼
Pipeline behaviors
(logging, workspace context, validation, transaction)
        │
        ▼
Handler
        │
        ├── Domain methods on Workflow / WorkflowVersion
        ├── IWorkflowRepository / IWorkflowQueries
        ├── IUnitOfWork / ITransaction
        └── IClock, IWorkspaceContext, IGuidGenerator
        │
        ▼
Infrastructure implementations
        │
        ▼
ApplicationDbContext → PostgreSQL
```

Rules:

- Queries use `IWorkflowQueries` only and are read-only.
- Handlers never use `DbContext` or `DbSet` directly.
- One transaction per command, except the two documented manual-transaction
  commands.
- Expected validation, not-found, and conflict outcomes return `Result`
  failures. Programmer errors and broken invariants still throw.

---

## 5. Architecture rules

- No EF Core, Npgsql, `ApplicationDbContext`, ASP.NET Core, MassTransit, or
  host references in `Workflows.Application`.
- No business rules in `Workflows.Infrastructure`.
- `IWorkspaceContext` is the only source of workspace and caller identity.
- Timestamps come from `IClock`; handlers never call `DateTime.UtcNow`.
- Mapping is manual. No AutoMapper or Mapster.
- No domain entities are exposed from handlers. The typed
  `WorkflowDefinition` in `WorkflowVersionDetailsDto` is a deliberate
  exception: it is an immutable value shape, not an entity.
- One transaction per command. The shared transaction behavior is used by
  `ITransactionalCommand<T>`; the two manual commands own their boundaries.
- Module tests stay in the module's test project. Cross-module composition
  belongs in `WorkflowAutomation.Integration.Tests`.

---

## 6. Current status

Phase 4 — Application/CQRS Control Plane.

Completed:

- Baseline gate: build and existing test suites verified.
- Three new projects created and registered in `WorkflowAutomation.slnx`.
- Project references and packages added.
- Messaging markers and `Unit`.
- Read-model DTOs and `PagedResult<T>`.
- Error vocabulary: `WorkflowErrors`, `ValidationError`,
  `DraftConflictError`.
- Persistence ports: `IUnitOfWork`, `ITransaction`, `IGuidGenerator`,
  `IWorkflowRepository`, `IWorkflowQueries`,
  `IWorkflowVersionNumberAllocator`, `UniqueConstraints`, and application
  exceptions.
- List query request contracts: `ListWorkflowsQuery` and
  `ListWorkflowExecutionsQuery`.

Next:

- MediatR registration and the four pipeline behaviors.
- Infrastructure implementations.
- Commands and queries.
- Application unit tests and integration coverage.
- Documentation/status synchronization after implementation.

Verification commands, run from the repository root:

```bash
dotnet build WorkflowAutomation.slnx

dotnet test tests/WorkflowAutomation.Workflows.Core.Tests/WorkflowAutomation.Workflows.Core.Tests.csproj
dotnet test tests/WorkflowAutomation.Workflows.Application.Tests/WorkflowAutomation.Workflows.Application.Tests.csproj
dotnet test tests/WorkflowAutomation.Integration.Tests/WorkflowAutomation.Integration.Tests.csproj
```

---

## 7. References

- `phase4_plan.md` — authoritative Phase 4 implementation contract.
- `phase3_plan.md` — domain, persistence, concurrency, and retention rules.
- `design_review.md` — architectural reasoning, including draft-conflict policy.
- `README.md` at the repository root — project overview and roadmap.
