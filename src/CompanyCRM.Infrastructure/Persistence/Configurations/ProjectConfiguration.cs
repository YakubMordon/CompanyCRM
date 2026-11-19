using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Description)
            .HasMaxLength(1000);

        builder.Property(p => p.ExternalOrgUrl)
            .HasMaxLength(500);

        builder.Property(p => p.ExternalProjectName)
            .HasMaxLength(100);

        builder.Property(p => p.ExternalPat)
            .HasMaxLength(500);

        builder.HasIndex(p => p.Code)
            .IsUnique()
            .HasDatabaseName("IX_Projects_Code");

        builder.HasMany(p => p.Members)
            .WithOne(m => m.Project)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.WorkItems)
            .WithOne(w => w.Project)
            .HasForeignKey(w => w.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.SyncConflicts)
            .WithOne(sc => sc.Project)
            .HasForeignKey(sc => sc.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.WeeklyPlans)
            .WithOne(wp => wp.Project)
            .HasForeignKey(wp => wp.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.CutoffConfigs)
            .WithOne(cc => cc.Project)
            .HasForeignKey(cc => cc.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
