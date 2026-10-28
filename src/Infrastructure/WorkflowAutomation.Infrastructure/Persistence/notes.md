# Workspace Query Filter Notes

## What `ApplyWorkspaceFilters` does
- Scans the EF Core model for all entities that implement `IMustHaveWorkspace`
- For each one, calls `ConfigureWorkspaceFilter<TEntity>` via reflection
- **Purpose:** automates filter setup — add `IMustHaveWorkspace` to any new entity and the filter is applied automatically, no manual wiring needed

## What `ConfigureWorkspaceFilter<TEntity>` does
- Adds a **global query filter**: `WHERE WorkspaceId = current workspace id`
- EF Core silently appends this to every query on that entity
- **Purpose:** ensures users only see data from their own workspace (multi-tenant isolation)

## Why reflection?
- `ConfigureWorkspaceFilter` needs a generic type `<TEntity>`, but we discover entity types at runtime
- `MakeGenericMethod` lets us call the generic method with the runtime type

## Key takeaway
Together, these methods give you **automatic, convention-based workspace isolation** — any entity marked with `IMustHaveWorkspace` is automatically filtered without extra configuration.
