using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.ToTable("WorkItems");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Code)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(w => w.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(w => w.Description)
            .HasMaxLength(4000);

        builder.Property(w => w.EstimatedHours)
            .HasPrecision(6, 2);

        builder.Property(w => w.SprintName)
            .HasMaxLength(100);

        builder.Property(w => w.ReviewLatestComment)
            .HasMaxLength(2000);

        builder.Property(w => w.ExternalId)
            .HasMaxLength(100);

        builder.Property(w => w.ExternalUrl)
            .HasMaxLength(500);

        builder.Property(w => w.LastSyncedContentHash)
            .HasMaxLength(64);

        builder.HasOne(w => w.Project)
            .WithMany(p => p.WorkItems)
            .HasForeignKey(w => w.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.CreatedBy)
            .WithMany(e => e.CreatedWorkItems)
            .HasForeignKey(w => w.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(w => w.Assignee)
            .WithMany(e => e.AssignedWorkItems)
            .HasForeignKey(w => w.AssigneeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(w => w.Reviewer)
            .WithMany(e => e.ReviewedWorkItems)
            .HasForeignKey(w => w.ReviewerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(w => w.Histories)
            .WithOne(h => h.WorkItem)
            .HasForeignKey(h => h.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(w => w.TimeEntries)
            .WithOne(t => t.WorkItem)
            .HasForeignKey(t => t.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(w => w.DailyPlanItems)
            .WithOne(dpi => dpi.WorkItem)
            .HasForeignKey(dpi => dpi.WorkItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(w => w.SyncConflicts)
            .WithOne(sc => sc.LocalWorkItem)
            .HasForeignKey(sc => sc.LocalWorkItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(w => new { w.ProjectId, w.Code })
            .HasDatabaseName("IX_WorkItems_ProjectId_Code");

        builder.HasIndex(w => new { w.ProjectId, w.Status })
            .HasDatabaseName("IX_WorkItems_ProjectId_Status");

        builder.HasIndex(w => new { w.ExternalSource, w.ExternalId })
            .HasDatabaseName("IX_WorkItems_ExternalSource_ExternalId");

        builder.HasIndex(w => w.AssigneeId)
            .HasDatabaseName("IX_WorkItems_AssigneeId");

        builder.HasIndex(w => w.ReviewerId)
            .HasDatabaseName("IX_WorkItems_ReviewerId");

        builder.HasIndex(w => w.CreatedById)
            .HasDatabaseName("IX_WorkItems_CreatedById");
    }
}
