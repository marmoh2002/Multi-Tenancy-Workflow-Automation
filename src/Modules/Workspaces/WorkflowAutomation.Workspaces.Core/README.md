# WorkflowAutomation.Workspaces.Core

The domain layer for the **Workspaces Module** in the Workflow Automation platform. This module defines the core multi-tenancy boundaries, workspace lifecycle, user-to-workspace memberships, and workspace-scoped connections.

---

## 1. Architectural Purpose & Module Boundaries

In this modular monolith, each workflow, execution history, and connection belongs to a specific workspace. 

- **Tenant Isolation**: Workspaces serve as the root boundary for multi-tenancy. No workflow can exist without an explicit `WorkspaceId`.
- **Zero Coupling to Identity**: As mandated by the architecture, cross-module relationships use raw `Guid` identifiers, never Entity Framework navigation properties. `WorkspaceMembership.UserId` is a raw `Guid`, ensuring `Workspaces.Core` has zero dependency on `Identity.Core` or `ApplicationUser`.
- **Pure Domain Library**: This project references only `WorkflowAutomation.Shared.Kernel` (for `AuditableEntity<TId>`, `Entity<TId>`, etc.) and has no dependencies on EF Core, ASP.NET Core, or MassTransit.

---

## 2. Components Built & The Need for Each

### Enums (`Enums/`)

| Enum | Values | Why It Is Needed |
| :--- | :--- | :--- |
| **`WorkspaceType`** | `Personal = 1`, `Organization = 2` | Distinguishes single-user private spaces from collaborative multi-user team spaces. A Personal workspace is auto-created on user registration, strictly restricted to 1 member (the owner), and cannot accept invites. Organization workspaces allow multi-user collaboration and role delegation. |
| **`WorkspaceStatus`** | `Active = 1`, `Suspended = 2`, `Archived = 3` | Governs the operational lifecycle of a workspace. Critical for **fail-closed authorization**: if an organization is `Suspended` (e.g., billing hold, administrative lock), API requests and workflow executions are immediately rejected with `403 Forbidden`. |
| **`WorkspaceRole`** | `Owner = 1`, `Admin = 2`, `Member = 3`, `Viewer = 4` | Defines Role-Based Access Control (RBAC) permissions. Roles are **scoped per workspace**, not globally in the user's JWT. A user can be an `Owner` in their Personal workspace, an `Admin` in Team A, and a `Viewer` in Team B. |

---

### Entities (`Entities/`)

#### 1. `Workspace` (`Entities/Workspace.cs`)
- **Inheritance**: `AuditableEntity<Guid>` (inherits `Id`, `CreatedAt`, `UpdatedAt`).
- **Core Properties**:
  - `Name` (`string`): Workspace display name.
  - `Description` (`string?`): Optional description.
  - `Type` (`WorkspaceType`): `Personal` or `Organization`.
  - `Status` (`WorkspaceStatus`): Current lifecycle state.
  - `CreatedByUserId` (`Guid`): Audit reference to the user who created the workspace.
- **Why It Is Needed**:
  - Serves as the primary tenant aggregate root.
  - Enforces domain invariants using dedicated factory methods (`CreatePersonal`, `CreateOrganization`) rather than public setters.
  - Provides explicit domain methods (`Suspend()`, `Activate()`, `Archive()`, `UpdateDetails()`) so state transitions are intentional and validated.

#### 2. `WorkspaceMembership` (`Entities/WorkspaceMembership.cs`)
- **Inheritance**: `AuditableEntity<Guid>`.
- **Core Properties**:
  - `WorkspaceId` (`Guid`): Foreign key to the `Workspace`.
  - `UserId` (`Guid`): Foreign key to the user account in Identity.
  - `Role` (`WorkspaceRole`): The user's role in this specific workspace.
  - `IsActive` (`bool`): Membership status toggle.
- **Why It Is Needed**:
  - Models the many-to-many relationship between users and workspaces.
  - Decoupled from the Identity module: by holding a raw `Guid UserId` instead of an `ApplicationUser` navigation property, changes to Identity schema cannot break workspace logic.
  - Allows membership role changes (`UpdateRole`) and deactivation (`Deactivate()`, `Activate()`) without deleting historical records.

#### 3. `WorkspaceConnection` (`Entities/WorkspaceConnection.cs` — Next Step)
- **Inheritance**: `AuditableEntity<Guid>`.
- **Core Properties**:
  - `WorkspaceId` (`Guid`): Owning workspace.
  - `Name` (`string`): Friendly connection name (e.g., *"Production Slack"*).
  - `Provider` (`string`): Integration provider (e.g., `"Slack"`, `"HttpApiKey"`, `"GitHub"`).
  - `EncryptedCredentials` (`string`): Ciphertext storing sensitive credentials.
  - `IsActive` (`bool`): Connection availability toggle.
- **Why It Is Needed**:
  - **Zero Secrets in Workflow Definitions**: Workflow DAG definitions are serialized and versioned; they must **never** store plaintext API tokens or secrets.
  - Step definitions reference a `ConnectionId`, and workers resolve and decrypt the credentials securely in-memory just-in-time during execution.

---

## 3. Role Permissions Summary

| Action | Owner | Admin | Member | Viewer |
| :--- | :---: | :---: | :---: | :---: |
| View workflows & run history | ✅ | ✅ | ✅ | ✅ |
| Create & edit workflows | ✅ | ✅ | ✅ | ❌ |
| Trigger workflow executions | ✅ | ✅ | ✅ | ❌ |
| Manage connections & credentials | ✅ | ✅ | ❌ | ❌ |
| Invite / remove members | ✅ | ✅ | ❌ | ❌ |
| Change member roles | ✅ | ✅ (up to Admin) | ❌ | ❌ |
| Delete workspace / Transfer ownership | ✅ | ❌ | ❌ | ❌ |
