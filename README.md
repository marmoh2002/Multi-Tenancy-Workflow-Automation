# Multi-Tenant Workflow Automation Platform

A .NET modular monolith where tenants create **DAG-based workflows** (via a React UI with a drag-and-drop editor) that execute asynchronously through RabbitMQ-driven background workers. Workflows are triggered by **webhooks**, **cron schedules**, or **manual clicks**.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Progress Tracker](#progress-tracker)
- [What's Been Built](#whats-been-built)
- [Getting Started](#getting-started)
- [Running Tests](#running-tests)
- [Planned Architecture](#planned-architecture)
- [Roadmap](#roadmap)

---

## Architecture Overview

The platform is designed as a **modular monolith** following the **Ports & Adapters** (Hexagonal Architecture) pattern. Modules communicate exclusively through message contracts — never through direct project references.

```
┌──────────────────────────────────────────────────────────┐
│                     React Frontend                       │
│              (DAG Editor + Real-time Dashboard)          │
└────────────────────────┬─────────────────────────────────┘
                         │ HTTP / WebSocket
┌────────────────────────▼─────────────────────────────────┐
│                    API Host (ASP.NET Core)                │
│   JWT Auth · Swagger · SignalR · Tenant Resolution       │
├──────────────────────────────────────────────────────────┤
│  Identity   │  Workflows   │  Execution  │ Notifications │
│  Module     │  Module      │  Module     │ Module        │
└──────────┬──┴──────────┬───┴──────┬──────┴───────────────┘
           │             │          │
           ▼             ▼          ▼
┌──────────────────────────────────────────────────────────┐
│              RabbitMQ (via MassTransit)                   │
│         Commands · Events · Transactional Outbox         │
└────────────┬─────────────────────────┬───────────────────┘
             │                         │
┌────────────▼─────────┐  ┌────────────▼───────────────────┐
│    Worker Service     │  │      Scheduler Service         │
│  Step Executors       │  │  Quartz.NET Cron Jobs          │
│  Saga State Machine   │  │  Trigger → StartWorkflow       │
└────────────┬─────────┘  └────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────┐
│                   PostgreSQL                              │
│         EF Core · Saga Persistence · Outbox              │
└──────────────────────────────────────────────────────────┘
```

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| **Runtime** | .NET 10 / C# |
| **Database** | PostgreSQL 15 (via Docker) |
| **Messaging** | RabbitMQ + MassTransit |
| **Scheduling** | Quartz.NET |
| **ORM** | Entity Framework Core |
| **Testing** | xUnit + Coverlet |
| **Containerization** | Docker / Docker Compose |
| **Frontend** (planned) | React + React Flow |

---

## Project Structure

```
Multi-Tenancy-Workflow-Automation/
├── DagWorkflow.slnx                    # Solution file (.NET slnx format)
├── docker-compose.yml                  # PostgreSQL service
├── README.md
│
├── DagWorkflow.Core/                   # Core domain library (zero infra dependencies)
│   ├── Definitions/
│   │   ├── WorkflowDefinition.cs       # Workflow aggregate — holds a list of steps
│   │   ├── StepDefinition.cs           # Step with Id, Type, DependsOn, Parameters
│   │   └── DagValidationResult.cs      # Validation result (Definitions namespace)
│   ├── DagValidationResult.cs          # Validation result (Core namespace)
│   ├── IDagValidator.cs                # Validator interface (strategy pattern)
│   ├── KahnDagValidator.cs             # DAG validator using Kahn's algorithm
│   └── WorkflowExecutionContext.cs     # Runtime context (logs + condition results)
│
├── DagWorkflow.Core.Tests/            # Unit tests
│   ├── DagValidatorTests.cs           # 7 test cases for DAG validation
│   └── ReadOnlyListExtensions.cs      # Test helper extensions
│
└── DagWorkflowPhase1/                 # Phase 1 placeholder (empty)
```

---

## Progress Tracker

### Phase 1 — Foundation (In Progress)

| Task | Status |
|------|--------|
| Solution structure & project scaffolding | ✅ Done |
| Domain models (`WorkflowDefinition`, `StepDefinition`) | ✅ Done |
| DAG validation with Kahn's algorithm | ✅ Done |
| `WorkflowExecutionContext` (runtime context shell) | ✅ Done |
| Unit tests for DAG validator (7 cases) | ✅ Done |
| Docker Compose for PostgreSQL | ✅ Done |
| Identity module (Users, Tenants, JWT) | ⬜ Not started |
| Workflows CRUD API | ⬜ Not started |
| API host with middleware pipeline | ⬜ Not started |
| EF Core integration + tenant query filters | ⬜ Not started |

### Phase 2 — Execution Engine

| Task | Status |
|------|--------|
| MassTransit + RabbitMQ setup | ⬜ Not started |
| Workflow execution saga (state machine) | ⬜ Not started |
| Step executors (HTTP, Delay, Conditional, Transform, Email, Log) | ⬜ Not started |
| Worker service | ⬜ Not started |
| Transactional outbox | ⬜ Not started |

### Phase 3 — Triggers & Reliability

| Task | Status |
|------|--------|
| Webhook ingestion + idempotency | ⬜ Not started |
| HTTP executor + Polly retry policies | ⬜ Not started |
| Retry layers + dead-letter queues | ⬜ Not started |
| Scheduler service (Quartz.NET) | ⬜ Not started |
| Integration tests (Testcontainers) | ⬜ Not started |

### Phase 4 — Observability & Notifications

| Task | Status |
|------|--------|
| Serilog + Seq structured logging | ⬜ Not started |
| OpenTelemetry (tracing + metrics) | ⬜ Not started |
| Grafana / Prometheus / Tempo | ⬜ Not started |
| SignalR real-time notifications | ⬜ Not started |

### Phase 5 — React Frontend

| Task | Status |
|------|--------|
| Auth pages | ⬜ Not started |
| Dashboard | ⬜ Not started |
| React Flow DAG builder | ⬜ Not started |
| Real-time execution status | ⬜ Not started |

---

## What's Been Built

### DAG Validator (`KahnDagValidator`)

The core algorithm that powers workflow validation. Uses **Kahn's topological sort** to verify that a workflow's step graph is a valid DAG (Directed Acyclic Graph).

**Validation pipeline:**

1. **Structural checks** — rejects empty or duplicate step IDs
2. **Dependency reference checks** — every `DependsOn` entry must point to an existing step; no duplicates allowed
3. **Graph construction** — builds an `adjacency` map (prerequisite → dependents) and an `inDegree` map (step → number of incoming dependencies)
4. **Queue-based BFS traversal** — processes nodes with `inDegree == 0` and decrements downstream in-degrees
5. **Cycle detection** — verifies if all nodes were visited; isolates stuck nodes if a cycle exists
6. **Topological ordering** — on success, returns a valid execution order

#### How Cycle Detection Works (Intuition & Mechanics)

Kahn's algorithm detects cycles through **in-degree starvation** (mathematical deadlock):

1. **In-Degree as a Dependency Counter:**
   Each node's `inDegree` represents the number of uncompleted prerequisites it must wait for.
   * `inDegree == 0` means all prerequisites are satisfied — the step is unlocked and enqueued to `ready`.
2. **Simulating Execution:**
   When a step is popped from `ready`, we append it to `orderedStepIds` and decrement the `inDegree` of all downstream steps that depend on it (`inDegree[dependent]--`). If any dependent reaches `0`, it is pushed to `ready`.
3. **The Cycle Trap (Deadlock):**
   * **Mathematical Law:** In any directed cycle (e.g., $A \rightarrow B \rightarrow A$ or $B \rightarrow C \rightarrow D \rightarrow B$), every node in the loop depends on another node in the loop.
   * Because every node in the loop is waiting on something else in the loop, **none of them can ever reach `inDegree == 0`**.
   * Consequently, **none of the cyclic nodes can ever enter the `ready` queue**.
4. **Detection Condition:**
   When the `ready` queue empties:
   $$\text{orderedStepIds.Count} \neq \text{workflow.Steps.Count}$$
   If the count of processed nodes is less than the total steps, a cycle is guaranteed to exist.
5. **Pinpointing Culprit Steps:**
   The validator identifies which steps caused the deadlock by finding all steps that were never visited:
   ```csharp
   var stuck = workflow.Steps
       .Select(step => step.Id)
       .Where(id => !processed.Contains(id))
       .ToList();
   ```
   This allows the platform to return actionable errors: `Cycle detected involving steps: B, C, D.`

**Example 1 — Diamond DAG (Valid):**
```
    A
   / \
  B   C
   \ /
    D
```
* Initial: `inDegree(A)=0`, `inDegree(B)=1`, `inDegree(C)=1`, `inDegree(D)=2`.
* `ready` starts with `[A]`.
* Process `A`: decrements `B` (now 0) and `C` (now 0) → `ready` has `[B, C]`.
* Process `B`: decrements `D` (now 1).
* Process `C`: decrements `D` (now 0) → `ready` has `[D]`.
* Process `D`: all 4 steps visited. **Valid DAG!**

**Example 2 — Circular Deadlock (Cycle):**
```
  A ───> B ───> C
         ▲      │
         └──────┘ (C depends on B, B depends on C)
```
* `inDegree(A)=0`, `inDegree(B)=2` (depends on A and C), `inDegree(C)=1` (depends on B).
* `ready` starts with `[A]`.
* Process `A`: decrements `B` from 2 to 1.
* `B` is still at 1 (waiting on `C`), so `B` **never enters `ready`**.
* `ready` is now empty. The loop halts.
* Processed: `[A]` (1 node). Total: 3 nodes.
* Cycle detected! Stuck nodes: `[B, C]`.

### Domain Models

- **`WorkflowDefinition`** — the workflow aggregate containing an ordered list of `StepDefinition`s
- **`StepDefinition`** — a single step with `Id`, `Type` (e.g., `"Log"`, `"HttpRequest"`), `DependsOn` (upstream dependencies), and `Parameters` (key-value configuration)
- **`DagValidationResult`** — result object with `IsValid`, `Errors`, and `OrderedStepIds`
- **`WorkflowExecutionContext`** — runtime context that accumulates execution logs and conditional branch results

### Test Coverage

8 xUnit test cases covering:

| Test | Validates |
|------|-----------|
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

All 7 DAG validation tests should pass.

---

## Planned Architecture

### Three Hosts

| Host | Type | Responsibility |
|------|------|----------------|
| **API** | ASP.NET Core Web API | HTTP endpoints, JWT auth, SignalR, Swagger |
| **Worker** | .NET Worker Service | Consumes messages, runs step executors, hosts the saga state machine |
| **Scheduler** | .NET Worker Service | Quartz.NET cron evaluation → publishes `StartWorkflowExecution` commands |

### Four Modules

| Module | Owns | Communicates via |
|--------|------|------------------|
| **Identity** | Users, Tenants, JWT tokens | `ITenantContext` interface |
| **Workflows** | Workflow definitions, DAG structure, webhooks | Publishes `StartWorkflowExecution` |
| **Execution** | Execution records, step results, saga state | Publishes step/execution lifecycle events |
| **Notifications** | SignalR hub | Consumes execution events → pushes to frontend |

### Six Step Types

| Type | Description |
|------|-------------|
| **HTTP Request** | Calls external APIs with Polly retry on transient failures |
| **Delay** | Pauses via MassTransit `ScheduleSend` (requeues after duration) |
| **Conditional** | Evaluates previous step output → picks a branch |
| **Transform** | Reshapes data between steps using JSONPath |
| **Email** | Sends email (MailHog in dev) |
| **Log** | Records a custom audit message |

### Key Design Decisions

- **Transactional Outbox** — DB write + outbox message in a single transaction to prevent "DB saved but message lost"
- **Optimistic Concurrency** — PostgreSQL `xmin` column as concurrency token for saga state
- **Three-layer Retry** — Polly (step-level) → MassTransit retry (consumer-level) → scheduled redelivery (persistent failures)
- **Tenant Isolation** — EF Core global query filter (`WHERE TenantId = @current`) on every entity
- **Idempotency** — Deduplication table with keys derived from trigger type (webhook/schedule/manual)

---

## Roadmap

| Phase | Weeks | Deliverable |
|-------|-------|-------------|
| **1. Foundation** | 1–2 | Solution structure, Identity module, Workflows CRUD, DAG validator, basic middleware |
| **2. Execution Engine** | 3–4 | MassTransit + saga, step executors, worker service, outbox |
| **3. Triggers & Reliability** | 5–6 | Webhooks, idempotency, HTTP executor + Polly, retry/DLQ, scheduler, integration tests |
| **4. Observability & Notifications** | 7–8 | Serilog/Seq, OpenTelemetry/Grafana, SignalR, full Docker Compose |
| **5. React Frontend** | 9–12 | Auth pages, dashboard, React Flow DAG builder, real-time status |

---

## License

*TBD*
