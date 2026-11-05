namespace WorkflowAutomation.Infrastructure.Configurations.Workflows;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowAutomation.Workflows.Core.Entities;
using WorkflowAutomation.Workflows.Core.Enums;
using WorkflowAutomation.Workspaces.Core.Entities;

public class WorkflowExecutionConfiguration : IEntityTypeConfiguration<WorkflowExecution>
{
    public void Configure(EntityTypeBuilder<WorkflowExecution> builder)
    {
        builder.ToTable("WorkflowExecutions", t =>
        {
            // 1. Name & Hash integrity
            t.HasCheckConstraint(
                "CK_WorkflowExecutions_WorkflowName_TrimmedAndNotEmpty",
                "\"WorkflowName\" = btrim(\"WorkflowName\", E' \\t\\n\\r\\f\\v') AND \"WorkflowName\" <> ''");

            // Validates exactly 64 lowercase hexadecimal characters
            t.HasCheckConstraint(
                "CK_WorkflowExecutions_DefinitionHash_Sha256Hex",
                "\"DefinitionHash\" ~ '^[0-9a-f]{64}$'");

            t.HasCheckConstraint(
                "CK_WorkflowExecutions_VersionNumber_Positive",
                "\"VersionNumber\" > 0");

            t.HasCheckConstraint(
                "CK_WorkflowExecutions_DefinitionSchemaVersion_Positive",
                "\"DefinitionSchemaVersion\" > 0");

            // Enum range guards for raw SQL inserts
            t.HasCheckConstraint(
                "CK_WorkflowExecutions_Status_Enum_Valid",
                $"\"Status\" IN ({(int)WorkflowExecutionStatus.Pending}, {(int)WorkflowExecutionStatus.Running}, {(int)WorkflowExecutionStatus.Completed}, {(int)WorkflowExecutionStatus.Failed}, {(int)WorkflowExecutionStatus.Cancelled})");

            t.HasCheckConstraint(
                "CK_WorkflowExecutions_Source_Enum_Valid",
                $"\"Source\" IN ({(int)WorkflowExecutionSource.Manual}, {(int)WorkflowExecutionSource.Scheduled}, {(int)WorkflowExecutionSource.Webhook})");

            // 2. Status & Timestamp Consistency
            t.HasCheckConstraint(
                "CK_WorkflowExecutions_Running_RequiresStartedAt",
                $"\"Status\" <> {(int)WorkflowExecutionStatus.Running} OR \"StartedAt\" IS NOT NULL");

            t.HasCheckConstraint(
                "CK_WorkflowExecutions_Completed_RequiresCompletedAt",
                $"\"Status\" <> {(int)WorkflowExecutionStatus.Completed} OR \"CompletedAt\" IS NOT NULL");

            t.HasCheckConstraint(
                "CK_WorkflowExecutions_Failed_RequiresCompletedAtAndReason",
                $"\"Status\" <> {(int)WorkflowExecutionStatus.Failed} OR (\"CompletedAt\" IS NOT NULL AND \"FailureReason\" IS NOT NULL AND btrim(\"FailureReason\", E' \\t\\n\\r\\f\\v') <> '')");

            // Name is under 63 bytes (PostgreSQL max identifier length)
            t.HasCheckConstraint(
                "CK_WorkflowExecutions_Cancelled_RequiresTimestamps",
                $"\"Status\" <> {(int)WorkflowExecutionStatus.Cancelled} OR (\"CancelledAt\" IS NOT NULL AND \"CancellationRequestedAt\" IS NOT NULL)");

            t.HasCheckConstraint(
                "CK_WorkflowExecutions_FailedStepId_OnlyOnFailed",
                $"\"Status\" = {(int)WorkflowExecutionStatus.Failed} OR \"FailedStepId\" IS NULL");

            t.HasCheckConstraint(
                "CK_WorkflowExecutions_CancellationRequest_RequiresReason",
                "\"CancellationRequestedAt\" IS NULL OR (\"CancellationReason\" IS NOT NULL AND btrim(\"CancellationReason\", E' \\t\\n\\r\\f\\v') <> '')");

            t.HasCheckConstraint(
                "CK_WorkflowExecutions_DefinitionSnapshot_NotEmpty",
                "\"DefinitionSnapshot\" <> ''");

            t.HasCheckConstraint(
                "CK_WorkflowExecutions_DefinitionSnapshot_IsObject",
                "jsonb_typeof(\"DefinitionSnapshot\"::jsonb) = 'object'");
        });

        builder.HasKey(e => e.Id);

        // Alternate key for multi-tenant composite child FKs (e.g. WorkflowStepExecution)
        builder.HasAlternateKey(e => new { e.WorkspaceId, e.Id });

        builder.Property(e => e.WorkspaceId).IsRequired();
        builder.Property(e => e.WorkflowId).IsRequired();
        builder.Property(e => e.WorkflowVersionId).IsRequired();

        builder.Property(e => e.WorkflowName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.VersionNumber).IsRequired();
        builder.Property(e => e.DefinitionSchemaVersion).IsRequired();

        // Stored as text (NOT jsonb) to preserve byte-exact SHA-256 hash verification
        builder.Property(e => e.DefinitionSnapshot)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.DefinitionHash)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(e => e.Status).HasConversion<int>().IsRequired();
        builder.Property(e => e.Source).HasConversion<int>().IsRequired();

        builder.Property(e => e.RequestedByUserId).IsRequired(false);
        builder.Property(e => e.ScheduleId).IsRequired(false);

        builder.Property(e => e.ScheduledOccurrenceId)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(e => e.StartedAt).IsRequired(false);
        builder.Property(e => e.CompletedAt).IsRequired(false);

        builder.Property(e => e.FailedStepId)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(e => e.FailureReason)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(e => e.CancellationRequestedAt).IsRequired(false);
        builder.Property(e => e.CancellationReason)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(e => e.CancelledAt).IsRequired(false);

        // Store default removed: domain model always explicitly controls true/false
        builder.Property(e => e.IsCancellable).IsRequired();

        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired(false);

        builder.Ignore(e => e.FinishedAt);

        // Tenant foreign key
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(e => e.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => new { e.ScheduleId, e.ScheduledOccurrenceId })
            .IsUnique()
            .HasFilter("\"ScheduleId\" IS NOT NULL AND \"ScheduledOccurrenceId\" IS NOT NULL");

        builder.HasIndex(e => new { e.WorkspaceId, e.WorkflowId, e.CreatedAt });

        // Polling index ordered by CreatedAt
        builder.HasIndex(e => new { e.WorkspaceId, e.Status, e.CreatedAt });

        builder.HasIndex(e => e.WorkflowVersionId);
        builder.HasIndex(e => new { e.WorkspaceId, e.WorkflowId, e.Status });

        // Optimistic concurrency token
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();
    }
}