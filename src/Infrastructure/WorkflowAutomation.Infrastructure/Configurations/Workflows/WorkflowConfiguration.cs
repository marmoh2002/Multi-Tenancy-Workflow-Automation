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
        builder.ToTable("Workflows");

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
            .HasForeignKey(w => w.ActiveVersionId)
            .OnDelete(DeleteBehavior.Restrict);


        // current ActiveVersionId relationship checks that a version exists, 
        // but not that it belongs to the same workflow. Enforcing ownership 
        // at the database level requires a composite foreign key and 
        // corresponding principal key; we should account for that in 
        // WorkflowVersionConfiguration.

        builder.Property(w => w.CreatedAt)
.IsRequired();

        builder.Property(w => w.UpdatedAt)
            .IsRequired(false);
    }
}
