namespace WorkflowAutomation.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Entities;

public class WorkspaceConnectionConfiguration : IEntityTypeConfiguration<WorkspaceConnection>
{
    public void Configure(EntityTypeBuilder<WorkspaceConnection> builder)
    {
        builder.ToTable("WorkspaceConnections");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.WorkspaceId)
            .IsRequired();

        builder.Property(c => c.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Provider)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.EncryptedCredentials)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.Property(c => c.IsActive)
            .IsRequired();

        builder.Property(c => c.CreatedByUserId)
            .IsRequired();


        // Connection names must be unique within a workspace
        builder.HasIndex(c => new { c.WorkspaceId, c.Name })
            .IsUnique();

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(c => c.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
