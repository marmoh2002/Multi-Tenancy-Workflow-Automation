namespace WorkflowAutomation.Infrastructure.Configurations.Workflows;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Workflows.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Entities;

public class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.ToTable("Workflows", t =>
        {
            t.HasCheckConstraint(
                "CK_Workflows_Name_TrimmedAndNotEmpty",
                "\"Name\" = btrim(\"Name\", E' \\t\\n\\r\\f\\v') AND \"Name\" <> ''");

            t.HasCheckConstraint(
                "CK_Workflows_CreatedByUserId_NotEmpty",
                "\"CreatedByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            t.HasCheckConstraint(
                "CK_Workflows_IsActive_RequiresActiveVersionId",
                "NOT \"IsActive\" OR \"ActiveVersionId\" IS NOT NULL");
        });


        builder.HasKey(w => w.Id);

        builder.Property(w => w.WorkspaceId)
            .IsRequired();

        builder.Property(w => w.CreatedByUserId)
            .IsRequired();

        builder.Property(w => w.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(w => w.Description)
            .HasMaxLength(2000)
            .IsRequired(false);

        builder.Property(w => w.IsActive)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(w => w.ActiveVersionId).IsRequired(false); ;

        // Name must be unique within a workspace
        builder.HasIndex(w => new { w.WorkspaceId, w.Name })
            .IsUnique();

        builder.HasIndex(w => new { w.WorkspaceId, w.IsActive });

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(w => w.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(w => w.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WorkflowVersion>()
            .WithMany()
            .HasForeignKey(w => new { w.Id, w.ActiveVersionId })
            .HasPrincipalKey(v => new { v.WorkflowId, v.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(w => w.CreatedAt)
            .IsRequired();

        builder.Property(w => w.UpdatedAt)
            .IsRequired(false);


        builder.HasAlternateKey(w => new { w.WorkspaceId, w.Id });
        builder.Property<uint>("xmin")
          .HasColumnName("xmin")
          .HasColumnType("xid")
          .IsRowVersion();
    }
}
