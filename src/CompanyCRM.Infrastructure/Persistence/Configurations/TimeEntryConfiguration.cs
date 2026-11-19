using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyCRM.Infrastructure.Persistence.Configurations;

public class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("TimeEntries");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Notes)
            .HasMaxLength(1000);

        builder.HasOne(t => t.WorkItem)
            .WithMany(w => w.TimeEntries)
            .HasForeignKey(t => t.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Employee)
            .WithMany(e => e.TimeEntries)
            .HasForeignKey(t => t.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.EmployeeId, t.IsRunning })
            .HasDatabaseName("IX_TimeEntries_EmployeeId_IsRunning");

        builder.HasIndex(t => t.WorkItemId)
            .HasDatabaseName("IX_TimeEntries_WorkItemId");

        builder.HasIndex(t => t.LoggedDate)
            .HasDatabaseName("IX_TimeEntries_LoggedDate");
    }
}
