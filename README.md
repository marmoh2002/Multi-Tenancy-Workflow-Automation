# Multi-Tenant Workflow Automation Platform

A .NET 10 modular monolith where workspaces create **DAG-based workflows** that execute asynchronously through RabbitMQ-driven background workers. Workflows are triggered by **webhooks**, **cron schedules**, or **manual execution**.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [What's Been Built](#whats-been-built)
- [Getting Started](#getting-started)
- [Running Tests](#running-tests)
- [Design Decisions](#design-decisions)
- [Implementation Roadmap](#implementation-roadmap)

---

## Architecture Overview

The platform is a **modular monolith** with three separate runtime hosts sharing a single PostgreSQL database. Modules communicate through **message contracts** over RabbitMQ via MassTransit. Workspace isolation is enforced at every layer through EF Core global query filters and per-request workspace context resolution.

```
┌──────────────────────────────────────────────────────────┐
│                     Client Application                    │
└────────────────────────┬─────────────────────────────────┘
                         │ HTTP + JWT Bearer + X-Workspace-Id
┌────────────────────────▼─────────────────────────────────┐
│                    API Host (ASP.NET Core)                 │
│   JWT Auth · Workspace Middleware · Bus Outbox             │
├───────────┬────────────┬─────────────────────────────────┤
│ Identity  │ Workspaces │  Workflows                       │
│ Module    │ Module     │  Module                          │
└─────┬─────┴─────┬──────┴──────┬──────────────────────────┘
      │           │             │
      ▼           ▼             ▼
┌──────────────────────────────────────────────────────────┐
│              RabbitMQ (via MassTransit)                    │
│         Commands · Events · Transactional Outbox          │
└────────────┬─────────────────────────┬───────────────────┘
             │                         │
┌────────────▼─────────┐  ┌────────────▼───────────────────┐
│    Worker Host        │  │      Scheduler Host             │
│  Saga State Machine   │  │  Quartz.NET Cron Jobs           │
│  Step Executors       │  │  Occurrence → StartExecution    │
│  Consumer Outbox      │  │  Bus Outbox                     │
└────────────┬─────────┘  └────────────┬───────────────────┘
             │                         │
┌────────────▼─────────────────────────▼───────────────────┐
│                   PostgreSQL 15                            │
│    EF Core · Saga State · Outbox · Identity Tables        │
└──────────────────────────────────────────────────────────┘
```

### Three Hosts

| Host | Type | Responsibility |
|:---|:---|:---|
| **API** | ASP.NET Core Web API | HTTP endpoints, JWT authentication, workspace resolution middleware, Bus Outbox publishing |
| **Worker** | .NET Worker Service | MassTransit consumers, saga state machine, step executors, Consumer Outbox with inbox deduplication |
| **Scheduler** | .NET Worker Service | Quartz.NET cron evaluation, deterministic occurrence identity, Bus Outbox publishing |

### Three Modules

| Module | Owns | Key Entities |
|:---|:---|:---|
| **Identity** | User accounts, authentication, JWT tokens | `ApplicationUser`, `ApplicationRole` |
| **Workspaces** | Workspace lifecycle, membership, connections | `Workspace`, `WorkspaceMembership`, `WorkspaceConnection` |
| **Workflows** | Workflow definitions, versioning, execution, DAG validation | `Workflow`, `WorkflowVersion`, `WorkflowExecution`, `WorkflowStepExecution` |

---

## Tech Stack

| Layer | Technology |
|:---|:---|
| **Runtime** | .NET 10 / C# |
| **Database** | PostgreSQL 15 (via Docker) |
| **Messaging** | RabbitMQ + MassTransit 8 |
| **Scheduling** | Quartz.NET |
| **ORM** | Entity Framework Core |
| **Authentication** | ASP.NET Core Identity + JWT Bearer |
| **Testing** | xUnit |
| **Containerization** | Docker / Docker Compose |

---

## Project Structure

The target solution structure follows a modular monolith layout. Migration from the current `DagWorkflow` namespace to `WorkflowAutomation` is planned for Phase 1.

### Current State

```
Multi-Tenancy-Workflow-Automation/
├── DagWorkflow.slnx                    # Current solution file
├── docker-compose.yml                  # PostgreSQL 15
│
├── DagWorkflow.Core/                   # Pure domain library (net10.0, zero deps)
│   ├── Definitions/
│   │   ├── WorkflowDefinition.cs       # Workflow with IReadOnlyList<StepDefinition>
│   │   └── StepDefinition.cs           # Id, Type, DependsOn, Parameters
│   ├── DagValidationResult.cs          # IsValid, Errors, OrderedStepIds
│   ├── IDagValidator.cs                # Validator interface
│   ├── KahnDagValidator.cs             # Kahn's topological sort + cycle detection
│   └── WorkflowExecutionContext.cs     # Runtime context shell
│
└── DagWorkflow.Core.Tests/             # xUnit (net10.0)
    ├── DagValidatorTests.cs            # 8 passing tests
    └── ReadOnlyListExtensions.cs       # Test helper
```

### Target State

```
WorkflowAutomation.sln

src/
├── Shared/
│   ├── WorkflowAutomation.SharedKernel/               # Result<T>, Error, IClock, Entity base
│   └── WorkflowAutomation.Shared.Contracts/           # Pure C# message records (no MassTransit refs)
│
├── Modules/
│   ├── Identity/
│   │   ├── WorkflowAutomation.Identity.Core/          # ApplicationUser : IdentityUser<Guid>
│   │   ├── WorkflowAutomation.Identity.Application/   # ITokenService
│   │   └── WorkflowAutomation.Identity.Infrastructure/ # JWT, Identity DI registration
│   │
│   ├── Workspaces/
│   │   ├── WorkflowAutomation.Workspaces.Core/        # Workspace, WorkspaceMembership
│   │   ├── WorkflowAutomation.Workspaces.Application/ # IWorkspaceService, IWorkspaceContext
│   │   └── WorkflowAutomation.Workspaces.Infrastructure/ # Workspace EF configs, middleware
│   │
│   └── Workflows/
│       ├── WorkflowAutomation.Workflows.Core/         # Workflow, WorkflowVersion, DAG validation
│       ├── WorkflowAutomation.Workflows.Application/  # IWorkflowEngine, execution coordination
│       └── WorkflowAutomation.Workflows.Infrastructure/ # Saga, consumers, step executors
│
├── Infrastructure/
│   └── WorkflowAutomation.Infrastructure/             # ApplicationDbContext, outbox, migrations
│
└── Hosts/
    ├── WorkflowAutomation.Api/
    ├── WorkflowAutomation.Worker/
    └── WorkflowAutomation.Scheduler/

tests/
├── WorkflowAutomation.Workflows.Core.Tests/
├── WorkflowAutomation.Identity.Tests/
├── WorkflowAutomation.Workspaces.Tests/
├── WorkflowAutomation.Workflows.Application.Tests/
├── WorkflowAutomation.Worker.Tests/
└── WorkflowAutomation.Integration.Tests/
```

### Key Dependency Rules

- **SharedKernel**: Zero external NuGet dependencies. Pure .NET.
- **Shared.Contracts**: Pure C# records. No MassTransit, EF Core, or ASP.NET references.
- **Identity.Core → SharedKernel**: Only NuGet dependency is `Microsoft.Extensions.Identity.Stores` (lightweight, no EF).
- **Infrastructure → Identity.Core**: `ApplicationDbContext` inherits `IdentityDbContext<ApplicationUser>` — needs the type, not the services.
- **Cross-module references**: Use raw `Guid` foreign keys, never navigation properties between modules.

---

## What's Been Built

### DAG Validator (`KahnDagValidator`)

The core algorithm powering workflow validation. Uses **Kahn's topological sort** to verify that a workflow's step graph is a valid DAG.

**Validation pipeline:**

1. **Structural checks** — rejects empty or duplicate step IDs
2. **Dependency reference checks** — every `DependsOn` entry must point to an existing step
3. **Graph construction** — builds adjacency map and in-degree map
4. **Queue-based BFS traversal** — processes nodes with `inDegree == 0`, decrements downstream counts
5. **Cycle detection** — identifies stuck nodes if not all were visited
6. **Topological ordering** — returns a valid execution order on success

### Test Coverage

8 xUnit test cases covering:

| Test | Validates |
|:---|:---|
| `Empty_workflow_is_valid` | Zero-step workflow passes |
| `Valid_chain_is_valid_and_returns_expected_order` | Linear A→B→C produces correct ordering |
| `Valid_diamond_is_valid_and_respects_dependencies` | Fan-out/fan-in diamond respects all dependency constraints |
| `Disconnected_nodes_are_valid` | Independent subgraphs are allowed |
| `Single_node_is_valid` | Single-step workflow passes |
| `Simple_cycle_is_invalid` | A↔B mutual dependency is rejected |
| `Self_loop_is_invalid` | A→A self-reference is rejected |
| `Unknown_dependency_is_invalid` | Reference to non-existent step is rejected |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (for PostgreSQL)

### Start Infrastructure

```bash
cd Multi-Tenancy-Workflow-Automation
docker compose up -d
```

This starts PostgreSQL on `localhost:5432` with:
- **User:** `postgres`
- **Password:** `password`
- **Database:** `multi_tenancy_db`

### Build

```bash
dotnet build DagWorkflow.slnx
```

---

## Running Tests

```bash
dotnet test DagWorkflow.slnx
```

All 8 DAG validation tests should pass.

---

## Design Decisions

| Decision | Choice | Rationale |
|:---|:---|:---|
| **Workspace as primary scope** | `Workspace` (not `Tenant`) | Unified concept for personal and organization contexts. Each workflow belongs to exactly one workspace. |
| **Workflow versioning** | Immutable `WorkflowVersion` with serialized definition | Executions are pinned to the exact version used at trigger time. Published versions cannot be modified. |
| **Compensation** | Deferred to future phase | No compensation machinery in Phase 1. Failed steps block dependents; completed side effects are not auto-reversed. Compensation remains a planned future capability (Phase 10). |
| **Parallel branch failure** | Fail-fast | Stop scheduling new steps. Request cancellation of in-flight work. Wait for settlement before marking execution as failed. |
| **Distributed cancellation** | Shared cancellation state in PostgreSQL | Three-layer design: (1) `WorkflowStepExecution.CancellationRequestedAt` in PostgreSQL as durable source of truth with conditional UPDATE guard, (2) `CancelWorkflowStep` via RabbitMQ as low-latency notification hint (not sole record), (3) worker-local `ConcurrentDictionary<(Guid, string), CancellationTokenSource>` for in-process signalling. Worker crash recovery via MassTransit message redelivery + step executor idempotency keys. Conditional UPDATE guards on both cancellation requests and terminal status transitions enforce at-most-once semantics. Cancellation request ≠ operation cancelled — only the owning Worker determines the actual outcome. |
| **Authentication** | ASP.NET Core Identity + custom JWT token service | Standard JWT validation (signature, issuer, audience, expiration). Token carries user identity only — workspace role is resolved per-request from membership. |
| **Transactional outbox** | MassTransit EF Core Outbox | API/Scheduler use Bus Outbox; Worker uses Consumer Outbox with inbox deduplication. |
| **Saga concurrency** | PostgreSQL `xmin` optimistic concurrency | No separate saga DbContext. Saga state table uses PostgreSQL's internal `xmin` column as row version. |
| **Single database context** | Shared `ApplicationDbContext : IdentityDbContext` | One context for Identity, Workspaces, Workflows, saga state, and outbox tables. Single PostgreSQL database. |
| **Cross-module data access** | Raw `Guid` foreign keys | No navigation properties between modules. Cross-module queries go through application-layer interfaces. |
| **Message contracts** | Pure C# records in `Shared.Contracts` with `Execution.Commands` and `Execution.Events` namespaces | No MassTransit references. Saga correlation configured via `CorrelateById()` in the state machine. |

---

## Implementation Roadmap

| Phase | Milestone | Key Deliverables |
|:---|:---|:---|
| **1** | **Solution Structure & Core Migration** | Create `WorkflowAutomation.sln`. Establish all projects and dependency graph. Migrate `DagWorkflow.Core` → `Workflows.Core`. Verify 8 tests pass. |
| **2** | **Identity, Workspaces & Multi-Tenancy** | `ApplicationUser`, `Workspace`, `WorkspaceMembership`. `ApplicationDbContext`. EF configurations. Global query filters. |
| **3** | **Workflow & Version Persistence** | `Workflow`, `WorkflowVersion` (immutable definitions), `WorkflowExecution`, `WorkflowStepExecution` (including `CancellationRequestedAt` and `CancellationReason` columns). EF migrations. |
| **4** | **API Idempotency & Bus Outbox** | `IdempotencyRecord`. API-side Bus Outbox. Execution creation transaction with deduplication. |
| **5** | **Saga & Fail-Fast Orchestration** | `WorkflowExecutionSaga`. `IWorkflowEngine` DAG step selector. Fail-fast cancellation and settling logic. Conditional UPDATE guard for `CancellationRequestedAt` (`WHERE Status = 'Running' AND CancellationRequestedAt IS NULL`). Step-state guard prevents double-decrement of `ActiveRunningStepsCount`. |
| **6** | **Worker Host & Step Executors** | MassTransit worker host. Consumer Outbox. `IStepExecutor` interface. `LogStepExecutor`, `HttpStepExecutor`. Step executor idempotency keys (`{ExecutionId}:{StepId}:{AttemptNumber}`) for crash recovery. Conditional UPDATE for terminal status (`WHERE Status = 'Running'`). Two-path cancellation discovery: worker-local `ConcurrentDictionary` + DB polling. Double-check pattern. |
| **7** | **Scheduler Host & Quartz.NET** | Quartz.NET scheduled jobs. Deterministic occurrence identity. Misfire policies. |
| **8** | **API Endpoints & JWT Security** | ASP.NET Core API host. JWT bearer auth. Workspace middleware. Workflow and execution endpoints. |
| **9** | **Integration Testing** | Testcontainers (PostgreSQL + RabbitMQ). Concurrency, fail-fast, crash recovery (MassTransit redelivery, idempotent executor replay), conditional UPDATE guards, concurrent cancellation/completion, Consumer Outbox atomicity, and workspace isolation tests. |
| **10** | **Compensation & Rollback (Future)** | Compensating actions, `Compensating` saga state, compensation commands/events, reverse-dependency execution order, compensation retry policies. |

---

## License

*TBD*
