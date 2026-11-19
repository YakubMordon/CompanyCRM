using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class WorkItemHistoryConfiguration : IEntityTypeConfiguration<WorkItemHistory>
{
    public void Configure(EntityTypeBuilder<WorkItemHistory> builder)
    {
        builder.ToTable("WorkItemHistories");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(h => h.FieldName)
            .HasMaxLength(100);

        builder.Property(h => h.OldValue)
            .HasMaxLength(4000);

        builder.Property(h => h.NewValue)
            .HasMaxLength(4000);

        builder.Property(h => h.Note)
            .HasMaxLength(2000);

        builder.HasOne(h => h.WorkItem)
            .WithMany(w => w.Histories)
            .HasForeignKey(h => h.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.ChangedBy)
            .WithMany(e => e.HistoryEntries)
            .HasForeignKey(h => h.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.WorkItemId, h.ChangedAt })
            .HasDatabaseName("IX_WorkItemHistories_WorkItemId_ChangedAt");
    }
}
