namespace WorkflowAutomation.Infrastructure.Configurations.Workspaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Entities;

public class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("Workspaces");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(w => w.Description)
            .HasMaxLength(600);

        builder.Property(w => w.Type)
            .IsRequired();

        builder.Property(w => w.Status)
            .IsRequired();

        builder.Property(w => w.CreatedByUserId)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(w => w.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict); //account deactivation -> access revoke only
    }
}

