namespace WorkflowAutomation.Infrastructure.Configurations.Workflows;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowAutomation.Workflows.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Entities;

public class WorkflowVersionConfiguration : IEntityTypeConfiguration<WorkflowVersion>
{
    public void Configure(EntityTypeBuilder<WorkflowVersion> builder)
    {
        builder.ToTable("WorkflowVersions", t =>
        {
            t.HasCheckConstraint(
                "CK_WorkflowVersions_VersionNumber_Positive",
                "\"VersionNumber\" > 0");

            t.HasCheckConstraint(
                "CK_WorkflowVersions_DefinitionSchemaVersion_Positive",
                "\"DefinitionSchemaVersion\" > 0");

            t.HasCheckConstraint(
                "CK_WorkflowVersions_SerializedDefinition_IsObject",
                "jsonb_typeof(\"SerializedDefinition\") = 'object'");

            t.HasCheckConstraint(
                "CK_WorkflowVersions_PublicationTimestamp_Consistency",
                "(\"IsPublished\" = false AND \"PublishedAt\" IS NULL) OR (\"IsPublished\" = true AND \"PublishedAt\" IS NOT NULL)");

            t.HasCheckConstraint(
                "CK_WorkflowVersions_Published_NoValidationErrors",
                "NOT \"IsPublished\" OR \"ValidationErrorsJson\" IS NULL");

            t.HasCheckConstraint(
                "CK_WorkflowVersions_ValidationErrorsJson_IsArrayOrNull",
                "\"ValidationErrorsJson\" IS NULL OR jsonb_typeof(\"ValidationErrorsJson\") = 'array'");
        });

        builder.HasKey(v => v.Id);

        builder.Property(v => v.WorkspaceId)
            .IsRequired();

        builder.Property(v => v.WorkflowId)
            .IsRequired();

        builder.Property(v => v.VersionNumber)
            .IsRequired();

        builder.Property(v => v.DefinitionSchemaVersion)
            .IsRequired();

        builder.Property(v => v.IsPublished)
            .IsRequired();

        builder.Property(v => v.SerializedDefinition)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(v => v.ValidationErrorsJson)
            .HasColumnType("jsonb")
            .IsRequired(false);

        builder.Property(v => v.PublishedAt)
            .IsRequired(false);

        builder.Property(v => v.CreatedAt)
            .IsRequired();

        builder.Property(v => v.UpdatedAt)
            .IsRequired(false);

        // Version numbers must be unique within a single workflow
        builder.HasIndex(v => new { v.WorkflowId, v.VersionNumber })
            .IsUnique();

        // Alternate key enabling the composite foreign key from Workflow
        builder.HasAlternateKey(v => new { v.WorkflowId, v.Id });

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(v => v.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Workflow>()
            .WithMany()
            .HasForeignKey(v => new { v.WorkspaceId, v.WorkflowId })
            .HasPrincipalKey(w => new { w.WorkspaceId, w.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property<uint>("xmin")
        .HasColumnName("xmin")
        .HasColumnType("xid")
        .IsRowVersion();
    }

}
