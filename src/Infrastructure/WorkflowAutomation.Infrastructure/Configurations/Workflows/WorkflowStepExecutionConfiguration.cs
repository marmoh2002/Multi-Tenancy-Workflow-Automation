namespace WorkflowAutomation.Infrastructure.Configurations.Workflows;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowAutomation.Workflows.Core.Entities;
using WorkflowAutomation.Workflows.Core.Enums;
using WorkflowAutomation.Workspaces.Core.Entities;

public class WorkflowStepExecutionConfiguration : IEntityTypeConfiguration<WorkflowStepExecution>
{
    public void Configure(EntityTypeBuilder<WorkflowStepExecution> builder)
    {
        builder.ToTable("WorkflowStepExecutions", t =>
        {
            // 1. StepId and AttemptNumber integrity
            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_StepId_TrimmedAndNotEmpty",
                "\"StepId\" = btrim(\"StepId\", E' \\t\\n\\r\\f\\v') AND \"StepId\" <> ''");

            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_AttemptNumber_Positive",
                "\"AttemptNumber\" > 0");

            // 2. Status enum range guard
            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_Status_Enum_Valid",
                $"\"Status\" IN ({(int)WorkflowStepExecutionStatus.Pending}, {(int)WorkflowStepExecutionStatus.Running}, {(int)WorkflowStepExecutionStatus.Completed}, {(int)WorkflowStepExecutionStatus.Failed}, {(int)WorkflowStepExecutionStatus.Cancelled}, {(int)WorkflowStepExecutionStatus.Skipped})");

            // 3. Status & Timestamp Consistency
            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_Pending_NoTimestamps",
                $"\"Status\" <> {(int)WorkflowStepExecutionStatus.Pending} OR (\"StartedAt\" IS NULL AND \"CompletedAt\" IS NULL AND \"CancelledAt\" IS NULL)");

            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_Running_RequiresStartedAt",
                $"\"Status\" <> {(int)WorkflowStepExecutionStatus.Running} OR (\"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL AND \"CancelledAt\" IS NULL)");

            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_Completed_RequiresTimestamps",
                $"\"Status\" <> {(int)WorkflowStepExecutionStatus.Completed} OR (\"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL)");

            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_Failed_RequiresTimestampsAndReason",
                $"\"Status\" <> {(int)WorkflowStepExecutionStatus.Failed} OR (\"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND \"ErrorMessage\" IS NOT NULL AND btrim(\"ErrorMessage\", E' \\t\\n\\r\\f\\v') <> '')");

            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_Cancelled_RequiresCancelledAt",
                $"\"Status\" <> {(int)WorkflowStepExecutionStatus.Cancelled} OR \"CancelledAt\" IS NOT NULL");

            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_Skipped_RequiresCompletedAt",
                $"\"Status\" <> {(int)WorkflowStepExecutionStatus.Skipped} OR (\"StartedAt\" IS NULL AND \"CompletedAt\" IS NOT NULL)");

            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_FinishTimestamps_Exclusive",
                "\"CompletedAt\" IS NULL OR \"CancelledAt\" IS NULL");

            // 4. Conditional fields
            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_ErrorMessage_OnlyOnFailed",
                $"\"Status\" = {(int)WorkflowStepExecutionStatus.Failed} OR \"ErrorMessage\" IS NULL");

            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_OutputMetadata_OnlyOnCompleted",
                $"\"Status\" = {(int)WorkflowStepExecutionStatus.Completed} OR \"OutputMetadata\" IS NULL");

            t.HasCheckConstraint(
                "CK_WorkflowStepExecutions_IsRetryable_OnlyOnFailed",
                $"\"Status\" = {(int)WorkflowStepExecutionStatus.Failed} OR NOT \"IsRetryable\"");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.WorkspaceId).IsRequired();
        builder.Property(e => e.ExecutionId).IsRequired();

        builder.Property(e => e.StepId)
            .HasMaxLength(WorkflowStepExecution.MaxStepIdLength)
            .IsRequired();

        builder.Property(e => e.AttemptNumber).IsRequired();
        builder.Property(e => e.Status).HasConversion<int>().IsRequired();

        builder.Property(e => e.StartedAt).IsRequired(false);
        builder.Property(e => e.CompletedAt).IsRequired(false);
        builder.Property(e => e.CancelledAt).IsRequired(false);

        builder.Property(e => e.ErrorMessage)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(e => e.IsRetryable).IsRequired();

        builder.Property(e => e.OutputMetadata)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired(false);

        builder.Ignore(e => e.FinishedAt);

        // Tenant foreign key
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(e => e.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_WorkflowStepExecutions_Workspaces_WorkspaceId");

        // Composite foreign key to parent execution
        builder.HasOne<WorkflowExecution>()
            .WithMany()
            .HasForeignKey(e => new { e.WorkspaceId, e.ExecutionId })
            .HasPrincipalKey(e => new { e.WorkspaceId, e.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WorkflowStepExecutions_Executions_WorkspaceId_ExecutionId");

        // Indexes
        builder.HasIndex(e => new { e.ExecutionId, e.StepId, e.AttemptNumber })
            .IsUnique();

        builder.HasIndex(e => new { e.ExecutionId, e.Status });

        // Optimistic concurrency token
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();
    }
}