namespace WorkflowAutomation.Infrastructure.Configurations.Workspaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Entities;
public class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> builder)
    {
        builder.ToTable("WorkspaceMemberships");

        builder.HasKey(m => new { m.WorkspaceId, m.UserId });

        builder.Property(m => m.WorkspaceId)
            .IsRequired();

        builder.Property(m => m.UserId)
            .IsRequired();

        builder.Property(m => m.Role)
            .IsRequired();

        builder.Property(m => m.IsActive)
            .IsRequired();

        // Optimized for workspace bootstrap resolution
        builder.HasIndex(m => m.UserId);

        builder.HasOne<Workspace>()
                .WithMany()
                .HasForeignKey(m => m.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

