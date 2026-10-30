namespace WorkflowAutomation.Infrastructure.Interceptors;

/// <summary>
/// While the domain layer protects entity methods during execution, an application developer, buggy code, or reflection could attempt to mutate entity properties directly or delete entities via DbContext.Remove(...).
/// The interceptor sits at the EF Core persistence boundary
/// 1- It guarantees that draft-editing rules and published-version immutability cannot be bypassed on any tracked SaveChanges / SaveChangesAsync.
/// 2- It blocks independent deletion of published versions while permitting cascade deletion when the owning Workflow is being deleted in the same unit of work.
/// </summary>


using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WorkflowAutomation.Workflows.Core.Entities;

public sealed class WorkflowVersionImmutabilityInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> ImmutableProperties = new(StringComparer.Ordinal)
    {
        nameof(WorkflowVersion.Id),
        nameof(WorkflowVersion.WorkspaceId),
        nameof(WorkflowVersion.WorkflowId),
        nameof(WorkflowVersion.VersionNumber),
        nameof(WorkflowVersion.CreatedAt),
    };
    private static readonly HashSet<string> DraftEditableProperties = new(StringComparer.Ordinal)
    {
        nameof(WorkflowVersion.SerializedDefinition),
        nameof(WorkflowVersion.DefinitionSchemaVersion),
        nameof(WorkflowVersion.ValidationErrorsJson),
        nameof(WorkflowVersion.UpdatedAt),
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ValidateWorkflowVersions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkflowVersions(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
    private static void ValidateWorkflowVersions(DbContext? context)
    {
        if (context is null)
            return;

        context.ChangeTracker.DetectChanges();
        var entries = context.ChangeTracker
            .Entries<WorkflowVersion>()
            .Where(e => e.State is
                EntityState.Added or
                EntityState.Modified or
                EntityState.Deleted)
            .ToList();
        if (entries.Count == 0)
            return;

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    ValidateAddedVersion(entry);
                    break;
                case EntityState.Modified:
                    ValidateModifiedVersion(entry);
                    break;
                case EntityState.Deleted:
                    ValidateDeletedVersion(context, entry);
                    break;
            }
        }
    }
    private static void ValidateAddedVersion(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<WorkflowVersion> entry)
    {
        var version = entry.Entity;
        if (!version.IsPublished)
        {
            if (version.PublishedAt is not null)
            {
                throw new InvalidOperationException(
                    "An unpublished workflow version draft cannot have a PublishedAt timestamp.");
            }
        }
        else
        {
            if (version.PublishedAt is null)
            {
                throw new InvalidOperationException(
                    "A published workflow version must have a PublishedAt timestamp.");
            }
            if (version.ValidationErrorsJson is not null)
            {
                throw new InvalidOperationException(
                    "A published workflow version cannot have validation errors.");
            }
        }
    }
    private static void ValidateModifiedVersion(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<WorkflowVersion> entry)
    {
        // 1. Permanent immutable properties check for all versions
        foreach (var propertyName in ImmutableProperties)
        {
            var prop = entry.Property(propertyName);
            if (prop.IsModified && !Equals(prop.OriginalValue, prop.CurrentValue))
            {
                throw new InvalidOperationException(
                    $"Cannot modify immutable property '{propertyName}' on workflow version.");
            }
        }
        var wasPublished = (bool)entry.Property(nameof(WorkflowVersion.IsPublished)).OriginalValue!;
        // 2. If it was already published, no application property may change
        if (wasPublished)
        {
            foreach (var prop in entry.Properties)
            {
                if (prop.Metadata.IsConcurrencyToken)
                {
                    continue;
                }
                if (prop.IsModified && !Equals(prop.OriginalValue, prop.CurrentValue))
                {
                    throw new InvalidOperationException(
                        "Cannot modify or unpublish an already published workflow version.");
                }
            }
            return;
        }
        // 3. Originally a draft: check if transitioning to published or staying draft
        if (entry.Entity.IsPublished)
        {
            if (entry.Entity.PublishedAt is null)
            {
                throw new InvalidOperationException(
                    "A workflow version transitioning to published must have a PublishedAt timestamp.");
            }
            if (entry.Entity.ValidationErrorsJson is not null)
            {
                throw new InvalidOperationException(
                    "Cannot publish a workflow version with validation errors.");
            }
            foreach (var prop in entry.Properties)
            {
                if (prop.Metadata.IsConcurrencyToken)
                {
                    continue;
                }
                if (!prop.IsModified || Equals(prop.OriginalValue, prop.CurrentValue))
                {
                    continue;
                }
                var name = prop.Metadata.Name;
                if (name != nameof(WorkflowVersion.IsPublished) &&
                    name != nameof(WorkflowVersion.PublishedAt) &&
                    !DraftEditableProperties.Contains(name))
                {
                    throw new InvalidOperationException(
                        $"Property '{name}' cannot be modified during version publication.");
                }
            }
        }
        else
        {
            if (entry.Entity.PublishedAt is not null)
            {
                throw new InvalidOperationException(
                    "An unpublished workflow version draft cannot have a PublishedAt timestamp.");
            }
            foreach (var prop in entry.Properties)
            {
                if (prop.Metadata.IsConcurrencyToken)
                {
                    continue;
                }
                if (!prop.IsModified || Equals(prop.OriginalValue, prop.CurrentValue))
                {
                    continue;
                }
                var name = prop.Metadata.Name;
                if (!DraftEditableProperties.Contains(name))
                {
                    throw new InvalidOperationException(
                        $"Property '{name}' cannot be modified on an unpublished workflow version draft.");
                }
            }
        }
    }
    private static void ValidateDeletedVersion(
    DbContext context,
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<WorkflowVersion> entry)
    {
        var wasPublished = (bool)entry.Property(nameof(WorkflowVersion.IsPublished)).OriginalValue!;
        if (!wasPublished)
        {
            // Unpublished drafts may be deleted independently
            return;
        }

        // Published version deletion is permitted ONLY when its owning Workflow
        // is tracked as Deleted in the same ChangeTracker unit of work.
        var originalWorkflowId = (Guid)entry.Property(nameof(WorkflowVersion.WorkflowId)).OriginalValue!;
        var originalWorkspaceId = (Guid)entry.Property(nameof(WorkflowVersion.WorkspaceId)).OriginalValue!;

        var isOwningWorkflowDeleted = context.ChangeTracker
            .Entries<Workflow>()
            .Any(w => w.State == EntityState.Deleted
                   && Equals(w.Property(nameof(Workflow.Id)).OriginalValue, originalWorkflowId)
                   && Equals(w.Property(nameof(Workflow.WorkspaceId)).OriginalValue, originalWorkspaceId));

        if (!isOwningWorkflowDeleted)
        {
            throw new InvalidOperationException(
                "Cannot delete a published workflow version independently. " +
                "Published versions may only be deleted when their owning workflow is deleted in the same unit of work.");
        }
    }

}