using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class SyncConflictConfiguration : IEntityTypeConfiguration<SyncConflict>
{
    public void Configure(EntityTypeBuilder<SyncConflict> builder)
    {
        builder.ToTable("SyncConflicts");

        builder.HasKey(sc => sc.Id);

        builder.Property(sc => sc.ExternalId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(sc => sc.ExternalTitle)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(sc => sc.ExternalDescription)
            .HasMaxLength(4000);

        builder.Property(sc => sc.ExternalStatus)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(sc => sc.ExternalAssigneeEmail)
            .HasMaxLength(254);

        builder.Property(sc => sc.ResolutionNotes)
            .HasMaxLength(2000);

        builder.HasOne(sc => sc.Project)
            .WithMany(p => p.SyncConflicts)
            .HasForeignKey(sc => sc.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(sc => sc.LocalWorkItem)
            .WithMany(w => w.SyncConflicts)
            .HasForeignKey(sc => sc.LocalWorkItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(sc => sc.ResolvedBy)
            .WithMany()
            .HasForeignKey(sc => sc.ResolvedById)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(sc => new { sc.ProjectId, sc.Status })
            .HasDatabaseName("IX_SyncConflicts_ProjectId_Status");

        builder.HasIndex(sc => sc.LocalWorkItemId)
            .HasDatabaseName("IX_SyncConflicts_LocalWorkItemId");

        builder.HasIndex(sc => sc.ExternalId)
            .HasDatabaseName("IX_SyncConflicts_ExternalId");
    }
}
